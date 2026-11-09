/*
 *  WinSqueeze addition to Squeezelite (GPLv3, same terms as Squeezelite).
 *  Recorder: WinSqueeze asks for a recording through the shared memory "Local\squeezelite-rec-<mac>" (a file path and tags,
 *  sequence counter odd while it writes). The output thread copies the decoded PCM it is about to play (before EQ and volume)
 *  into a ring; a writer thread encodes it to 24-bit FLAC, so the audio thread never touches the disk. A new request with
 *  another path finishes the current file and starts the next one (WinSqueeze splits on song changes). Song changes are announced late,
 *  so the writer keeps cut_earlier_ms of audio back and a next-file cut lands that far before the request.
 */
#include "squeezelite.h"
#include <FLAC/stream_encoder.h>
#include <FLAC/metadata.h>

#if VISEXPORT && WIN

#define REC_PATH_CHARS 1024
#define REC_TAG_BYTES 4096
#define REC_RING_FRAMES (1 << 21) // ~11 s even at 192 kHz, so a slow disk never costs audio

struct rec_win_t {
	volatile LONG sequence;   // odd while WinSqueeze is updating
	u32_t request;            // WinSqueeze bumps it for every start / next file / stop
	u32_t active;             // 1 = record into path, 0 = stop
	u32_t applied;            // last request Squeezelite acted on
	u32_t error;              // 0 ok, 1 could not create the file, 2 encoder failed
	u32_t rate;               // rate of the file being written
	s64_t frames_written;     // in the current file
	s64_t dropped_frames;     // ring overflow (disk too slow)
	wchar_t path[REC_PATH_CHARS];
	char tags[REC_TAG_BYTES]; // UTF-8 "NAME=value\n" lines (Vorbis comments)
	u32_t cut_earlier_ms;     // stations announce songs late: a "next file" cut goes this far back from the moment it's asked for
};

static struct rec_win_t *rec_map = NULL;
static HANDLE rec_handle = NULL;
static s32_t *ring = NULL;
static volatile LONG64 ring_write = 0, ring_read = 0; // frame counters; the ring index is counter % REC_RING_FRAMES
static volatile LONG capturing = 0;
static volatile LONG capture_rate = 0, rate_change_rate = 0;
static volatile LONG64 rate_change_at = -1; // frame counter where the sample rate changed (the file is split there)
static log_level loglevel;

// Output thread: copy what is about to play into the ring (never waits)
void rec_capture(s32_t *samples, frames_t frames, unsigned rate) {
	if (!capturing || !ring) return;
	LONG64 write_pos = ring_write;
	if (write_pos - ring_read + frames > REC_RING_FRAMES) { if (rec_map) rec_map->dropped_frames += frames; return; }
	if ((LONG)rate != capture_rate) { rate_change_rate = rate; rate_change_at = write_pos; capture_rate = rate; }
	while (frames) {
		frames_t index = (frames_t)(write_pos % REC_RING_FRAMES), chunk = min(frames, REC_RING_FRAMES - index);
		memcpy(ring + index * 2, samples, chunk * 2 * sizeof(s32_t));
		samples += chunk * 2; write_pos += chunk; frames -= chunk;
	}
	InterlockedExchange64(&ring_write, write_pos);
}

static FLAC__StreamEncoder *encoder = NULL;
static FLAC__StreamMetadata *metadata[2];
static FLAC__int32 encode_buf[8192 * 2];
static wchar_t file_path[REC_PATH_CHARS], part_path[REC_PATH_CHARS];
static char file_tags[REC_TAG_BYTES];
static int part = 1;

static void close_file(void) {
	if (!encoder) return;
	FLAC__stream_encoder_finish(encoder); // also closes the FILE
	FLAC__stream_encoder_delete(encoder); encoder = NULL;
	FLAC__metadata_object_delete(metadata[0]); FLAC__metadata_object_delete(metadata[1]);
}

static bool open_file(unsigned rate) {
	// A sample-rate change mid-recording carries on in "<name> (part 2).flac"
	if (part == 1) wcscpy(part_path, file_path);
	else { wchar_t *dot = wcsrchr(file_path, L'.'); size_t stem = dot ? (size_t)(dot - file_path) : wcslen(file_path); _snwprintf(part_path, REC_PATH_CHARS - 1, L"%.*ls (part %d)%ls", (int)stem, file_path, part, dot ? dot : L""); }
	FILE *file = _wfopen(part_path, L"w+b");
	if (!file) { rec_map->error = 1; LOG_WARN("recorder: could not create file"); return false; }
	encoder = FLAC__stream_encoder_new();
	FLAC__stream_encoder_set_channels(encoder, 2);
	FLAC__stream_encoder_set_bits_per_sample(encoder, 24);
	FLAC__stream_encoder_set_sample_rate(encoder, rate);
	FLAC__stream_encoder_set_compression_level(encoder, 5);
	metadata[0] = FLAC__metadata_object_new(FLAC__METADATA_TYPE_VORBIS_COMMENT);
	for (char *line = file_tags; *line; ) {
		char *end = strchr(line, '\n'), *equals = strchr(line, '=');
		if (end) *end = 0;
		if (equals && equals > line) {
			FLAC__StreamMetadata_VorbisComment_Entry entry;
			*equals = 0;
			if (FLAC__metadata_object_vorbiscomment_entry_from_name_value_pair(&entry, line, equals + 1)) FLAC__metadata_object_vorbiscomment_append_comment(metadata[0], entry, false);
			*equals = '=';
		}
		if (!end) break;
		*end = '\n'; line = end + 1;
	}
	metadata[1] = FLAC__metadata_object_new(FLAC__METADATA_TYPE_PADDING); metadata[1]->length = 8192; // room to edit tags later without rewriting
	FLAC__stream_encoder_set_metadata(encoder, metadata, 2);
	if (FLAC__stream_encoder_init_FILE(encoder, file, NULL, NULL) != FLAC__STREAM_ENCODER_INIT_STATUS_OK) {
		LOG_WARN("recorder: encoder init failed"); FLAC__stream_encoder_delete(encoder); encoder = NULL; // delete also closes the file
		FLAC__metadata_object_delete(metadata[0]); FLAC__metadata_object_delete(metadata[1]); rec_map->error = 2; return false;
	}
	rec_map->rate = rate; rec_map->frames_written = 0; rec_map->error = 0;
	LOG_INFO("recorder: writing %u Hz FLAC", rate);
	return true;
}

// Encode ring frames up to the counter "until" into the open file (opening it at the right rate first)
static void drain(LONG64 until) {
	while (ring_read < until) {
		if (rate_change_at >= 0 && ring_read >= rate_change_at) { // a new rate starts here
			if (encoder) { close_file(); part++; }
			rate_change_at = -1;
		}
		LONG64 change_at = rate_change_at, stop = until; if (change_at > ring_read && change_at < stop) stop = change_at;
		if (!encoder && file_path[0] && !open_file(capture_rate ? capture_rate : 44100)) { file_path[0] = 0; ring_read = until; return; }
		while (ring_read < stop) {
			frames_t index = (frames_t)(ring_read % REC_RING_FRAMES), chunk = (frames_t)min(stop - ring_read, (LONG64)min(8192, REC_RING_FRAMES - index));
			if (encoder) {
				for (frames_t sample = 0; sample < chunk * 2; sample++) encode_buf[sample] = ring[index * 2 + sample] >> 8; // 32-bit internal to 24-bit
				if (!FLAC__stream_encoder_process_interleaved(encoder, encode_buf, chunk)) { rec_map->error = 2; close_file(); file_path[0] = 0; }
				else rec_map->frames_written += chunk;
			}
			InterlockedExchange64(&ring_read, ring_read + chunk);
		}
	}
}

// Audio kept back from the file so a song-change cut can still land before it (capped at half the ring)
static LONG64 held_back(void) {
	LONG64 frames = (LONG64)rec_map->cut_earlier_ms * (capture_rate ? capture_rate : 44100) / 1000;
	return min(frames, (LONG64)(REC_RING_FRAMES / 2));
}

static DWORD WINAPI rec_thread(LPVOID unused) {
	u32_t seen_request = 0;
	while (true) {
		Sleep(100);
		LONG sequence = rec_map->sequence;
		if (!(sequence & 1) && rec_map->request != seen_request) {
			u32_t request = rec_map->request, active = rec_map->active;
			wchar_t new_path[REC_PATH_CHARS]; char new_tags[REC_TAG_BYTES];
			memcpy(new_path, rec_map->path, sizeof(new_path)); new_path[REC_PATH_CHARS - 1] = 0;
			memcpy(new_tags, rec_map->tags, sizeof(new_tags)); new_tags[REC_TAG_BYTES - 1] = 0;
			if (rec_map->sequence == sequence) { // consistent copy
				bool next_song = active && new_path[0] && capturing && file_path[0]; // carrying on into the next song: cut back by the hold-back; start / stop take everything
				LONG64 cut_at = ring_write - (next_song ? held_back() : 0); if (cut_at < ring_read) cut_at = ring_read;
				drain(cut_at); close_file(); // what played before the cut belongs to the previous file
				seen_request = request; part = 1;
				if (active && new_path[0] && !ring) ring = malloc(REC_RING_FRAMES * 2 * sizeof(s32_t)); // only once something is recorded
				if (active && new_path[0] && ring) { wcscpy(file_path, new_path); strcpy(file_tags, new_tags); if (!next_song) InterlockedExchange64(&ring_read, ring_write); InterlockedExchange(&capturing, 1); } // a fresh start skips anything stale; the next song keeps the held-back audio
				else { file_path[0] = 0; InterlockedExchange(&capturing, 0); }
				rec_map->applied = request;
			}
		}
		drain(ring_write - (capturing && file_path[0] ? held_back() : 0));
	}
	return 0;
}

void rec_init(log_level level, u8_t *mac) {
	char name[64];
	loglevel = level;
	sprintf(name, "Local\\squeezelite-rec-%02x%02x%02x%02x%02x%02x", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
	rec_handle = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, sizeof(struct rec_win_t), name);
	if (rec_handle) rec_map = (struct rec_win_t *)MapViewOfFile(rec_handle, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(struct rec_win_t));
	if (!rec_map) { LOG_WARN("recorder: unable to open shared memory"); rec_map = NULL; return; }
	memset(rec_map, 0, sizeof(struct rec_win_t));
	LOG_INFO("opened recorder shared memory as %s", name);
	CreateThread(NULL, 0, rec_thread, NULL, 0, NULL);
}

#endif
