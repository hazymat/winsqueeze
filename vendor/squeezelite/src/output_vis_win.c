/*
 *  WinSqueeze addition to Squeezelite (GPLv3, same terms as Squeezelite).
 *  Windows version of the visualiser export (-v): the samples being played go into a named shared-memory ring
 *  "Local\squeezelite-vis-<mac>", so WinSqueeze can drive its meters. A sequence counter (odd while writing) replaces
 *  the Linux rwlock, so the audio thread never waits on the reader.
 */
#include "squeezelite.h"

#if VISEXPORT && WIN

#define VIS_BUF_SIZE 16384 // samples (interleaved L/R s16), about 186 ms at 44.1 kHz

struct vis_win_t {
	volatile LONG sequence;   // odd while the writer is updating
	u32_t buf_size;           // VIS_BUF_SIZE
	u32_t buf_index;          // next sample to write
	u32_t running;            // 0 when output is silent / stopped
	u32_t rate;               // sample rate of what is in the buffer
	u32_t reserved;
	s64_t updated_ms;         // GetTickCount64 at last write
	s16_t buffer[VIS_BUF_SIZE];
};

static struct vis_win_t *vis_map = NULL;
static HANDLE vis_handle = NULL;
static log_level loglevel;

void _vis_export(struct buffer *outputbuf, struct outputstate *output, frames_t out_frames, bool silence) {
	if (!vis_map) return;
	InterlockedIncrement(&vis_map->sequence);
	if (silence) {
		vis_map->running = 0;
	} else {
		frames_t vis_cnt = out_frames;
		s32_t *ptr = (s32_t *) outputbuf->readp;
		unsigned i = vis_map->buf_index;
		while (vis_cnt--) {
			s32_t left = *(ptr++), right = *(ptr++);
			if (output->current_replay_gain) { left = gain(left, output->current_replay_gain); right = gain(right, output->current_replay_gain); }
			vis_map->buffer[i++] = (s16_t)(left >> 16);
			vis_map->buffer[i++] = (s16_t)(right >> 16);
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
	} else {
		LOG_WARN("unable to open visualiser shared memory");
	}
}

#endif
