/*
 *  WinSqueeze addition to Squeezelite (GPLv3, same terms as Squeezelite).
 *  Windows version of the visualiser export (-v): the samples being played go into a named shared-memory ring
 *  "Local\squeezelite-vis-<mac>", so WinSqueeze can drive its meters. A sequence counter (odd while writing) replaces
 *  the Linux rwlock, so the audio thread never waits on the reader.
 *  Two rings: "buffer" holds the audio after the EQ but before the volume, "raw" the audio before the EQ; "volume" is the
 *  player's volume gain (16.16 fixed), so WinSqueeze can choose whether its meters follow the EQ and/or the volume.
 */
#include "squeezelite.h"

#if VISEXPORT && WIN

#define VIS_BUF_SIZE 262144 // samples (interleaved L/R s16): 680 ms even at 192 kHz, so the reader can look back past the output latency

struct vis_win_t {
	volatile LONG sequence;   // odd while the writer is updating
	u32_t buf_size;           // VIS_BUF_SIZE
	u32_t buf_index;          // next sample to write
	u32_t running;            // 0 when output is silent / stopped
	u32_t rate;               // sample rate of what is in the buffer
	u32_t volume;             // volume gain, 16.16 fixed (0x10000 = full); not applied to either ring
	s64_t updated_ms;         // GetTickCount64 at last write
	s16_t buffer[VIS_BUF_SIZE]; // after the EQ, before the volume
	s16_t raw[VIS_BUF_SIZE];    // before the EQ
};
static bool vis_captured = false; // this period's frames were already written by vis_capture

static struct vis_win_t *vis_map = NULL;
static HANDLE vis_handle = NULL;
static log_level loglevel;

// Called from the PortAudio write path before the EQ (post_eq false) and again after it, before the volume; writes ahead of
// buf_index, which _vis_export then advances, so the reader never sees a half-written period
void vis_capture(s32_t *samples, frames_t frames, s32_t replay_gain, bool post_eq) {
	if (!vis_map) return;
	s16_t *ring = post_eq ? vis_map->buffer : vis_map->raw;
	unsigned i = vis_map->buf_index;
	while (frames--) {
		s32_t left = *(samples++), right = *(samples++);
		if (replay_gain) { left = gain(left, replay_gain); right = gain(right, replay_gain); }
		ring[i++] = (s16_t)(left >> 16);
		ring[i++] = (s16_t)(right >> 16);
		if (i >= VIS_BUF_SIZE) i = 0;
	}
	if (post_eq) vis_captured = true;
}

void _vis_export(struct buffer *outputbuf, struct outputstate *output, frames_t out_frames, bool silence) {
	if (!vis_map) return;
	InterlockedIncrement(&vis_map->sequence);
	vis_map->volume = (u32_t)(((s64_t)output->gainL + output->gainR) / 2);
	if (silence) {
		vis_map->running = 0;
		vis_captured = false;
	} else if (vis_captured) {
		vis_captured = false;
		vis_map->buf_index = (vis_map->buf_index + out_frames * 2) % VIS_BUF_SIZE;
		vis_map->rate = output->current_sample_rate;
		vis_map->updated_ms = (s64_t)GetTickCount64();
		vis_map->running = 1;
	} else { // not captured (e.g. DSD): the played samples go in both rings
		frames_t vis_cnt = out_frames;
		s32_t *ptr = (s32_t *) outputbuf->readp;
		unsigned i = vis_map->buf_index;
		while (vis_cnt--) {
			s32_t left = *(ptr++), right = *(ptr++);
			if (output->current_replay_gain) { left = gain(left, output->current_replay_gain); right = gain(right, output->current_replay_gain); }
			vis_map->raw[i] = vis_map->buffer[i] = (s16_t)(left >> 16); i++;
			vis_map->raw[i] = vis_map->buffer[i] = (s16_t)(right >> 16); i++;
			if (i >= VIS_BUF_SIZE) i = 0;
		}
		vis_map->buf_index = i;
		vis_map->rate = output->current_sample_rate;
		vis_map->updated_ms = (s64_t)GetTickCount64();
		vis_map->running = 1;
	}
	InterlockedIncrement(&vis_map->sequence);
}

void vis_stop(void) {
	if (!vis_map) return;
	InterlockedIncrement(&vis_map->sequence);
	vis_map->running = 0;
	InterlockedIncrement(&vis_map->sequence);
}

void output_vis_init(log_level level, u8_t *mac) {
	char name[64];
	loglevel = level;
	sprintf(name, "Local\\squeezelite-vis-%02x%02x%02x%02x%02x%02x", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
	vis_handle = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, sizeof(struct vis_win_t), name);
	if (vis_handle) vis_map = (struct vis_win_t *)MapViewOfFile(vis_handle, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(struct vis_win_t));
	if (vis_map) {
		memset(vis_map, 0, sizeof(struct vis_win_t));
		vis_map->buf_size = VIS_BUF_SIZE;
		vis_map->rate = 44100;
		LOG_INFO("opened visualiser shared memory as %s", name);
		eq_init(mac); // WinSqueeze graphic equaliser rides on the same switch
		rec_init(level, mac); // and so does the recorder
	} else {
		LOG_WARN("unable to open visualiser shared memory");
	}
}

#endif
