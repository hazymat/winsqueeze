/*
 *  WinSqueeze addition to Squeezelite (GPLv3, same terms as Squeezelite).
 *  Graphic equaliser: WinSqueeze writes biquad coefficients for the current sample rate into the shared memory
 *  "Local\squeezelite-eq-<mac>" (sequence counter odd while it writes); the output thread runs them over the PCM it is
 *  about to play. When disabled (or the coefficients are for another rate) the samples are untouched, so bypass is bit-perfect.
 */
#include "squeezelite.h"

#if VISEXPORT && WIN

#define EQ_MAX_STAGES 12

struct eq_win_t {
	volatile LONG sequence;   // odd while WinSqueeze is updating
	u32_t enabled;            // 0 = bypass
	u32_t rate;               // sample rate the coefficients were designed for
	u32_t stages;             // biquads per channel
	float preamp;             // linear gain before the filters (headroom for boosts)
	float reserved;
	float coeffs[2][EQ_MAX_STAGES][5]; // per channel: b0 b1 b2 a1 a2 (a0 = 1)
};

static struct eq_win_t *eq_map = NULL;
static HANDLE eq_handle = NULL;
static LONG seen_sequence = -1;
static u32_t active = 0, active_rate = 0, active_stages = 0;
static double preamp = 1.0, coeffs[2][EQ_MAX_STAGES][5], state[2][EQ_MAX_STAGES][2];

// Take a consistent copy of new settings; filter memory is cleared when the EQ switches on or its shape changes size
static void eq_refresh(void) {
	LONG sequence = eq_map->sequence;
	if (sequence == seen_sequence || (sequence & 1)) return;
	u32_t enabled = eq_map->enabled, rate = eq_map->rate, stages = min(eq_map->stages, EQ_MAX_STAGES);
	double new_preamp = eq_map->preamp, new_coeffs[2][EQ_MAX_STAGES][5];
	for (int channel = 0; channel < 2; channel++) for (u32_t stage = 0; stage < stages; stage++) for (int k = 0; k < 5; k++) new_coeffs[channel][stage][k] = eq_map->coeffs[channel][stage][k];
	if (eq_map->sequence != sequence) return; // writer was mid-update: try again next block
	if (!active || stages != active_stages) memset(state, 0, sizeof(state));
	memcpy(coeffs, new_coeffs, sizeof(coeffs));
	preamp = new_preamp; active = enabled && stages > 0; active_rate = rate; active_stages = stages; seen_sequence = sequence;
}

void eq_process(s32_t *samples, frames_t frames, unsigned rate) {
	if (!eq_map) return;
	eq_refresh();
	if (!active || active_rate != rate) return;
	while (frames--) {
		for (int channel = 0; channel < 2; channel++) {
			double value = samples[channel] / 2147483648.0 * preamp;
			for (u32_t stage = 0; stage < active_stages; stage++) { // transposed direct form II
				double *c = coeffs[channel][stage], *z = state[channel][stage];
				double out = c[0] * value + z[0];
				z[0] = c[1] * value - c[3] * out + z[1];
				z[1] = c[2] * value - c[4] * out;
				value = out;
			}
			value *= 2147483648.0;
			samples[channel] = value >= 2147483647.0 ? 0x7fffffff : value <= -2147483648.0 ? (s32_t)0x80000000 : (s32_t)value;
		}
		samples += 2;
	}
}

void eq_init(u8_t *mac) {
	char name[64];
	sprintf(name, "Local\\squeezelite-eq-%02x%02x%02x%02x%02x%02x", mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
	eq_handle = CreateFileMappingA(INVALID_HANDLE_VALUE, NULL, PAGE_READWRITE, 0, sizeof(struct eq_win_t), name);
	if (eq_handle) eq_map = (struct eq_win_t *)MapViewOfFile(eq_handle, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(struct eq_win_t));
	if (eq_map) memset(eq_map, 0, sizeof(struct eq_win_t));
}

#endif
