# Lyrion display graphics

From Lyrion Music Server's `Graphics` folder (https://github.com/LMS-Community/slimserver, originally Slim Devices), used in Transporter mode
with the Lyrion maintainers' blessing (Michael Herger, Nov 2026):

- `standard.2.font.bmp`: the big-line font of the Squeezebox / Transporter displays. `tools/lyrion_font.py` turns it into `ui/src/parts/lyrionFont.ts`.
- `vumeter.bmp`: the Transporter's analog VU meter, 48 needle positions of 160 x 32 dots. `tools/lyrion_vu.py` turns it into `ui/src/parts/lyrionVu.ts`.
