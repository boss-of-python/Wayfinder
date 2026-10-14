# Device qualification plan — not measured results

Owner must nominate actual models/OS builds and provide device access. None is
currently connected. Android 10+ and iOS 16+ remain provisional floors.

| Family | Planning anchor | Target | Evidence status |
|---|---|---|---|
| Older Android | lowest supported device, TBD | Battery 30 FPS | Not started |
| Mid-range Qualcomm | Snapdragon 778G class, 6 GB | Balanced 60 FPS | Not started |
| Mid-range MediaTek | Dimensity 1080 class, 6 GB | Balanced 60 FPS | Not started |
| High-end Android | owner-nominated model | High/Balanced 60 FPS | Not started |
| Smallest iPhone | smallest supported screen, TBD | readability + stable tier | Not started |
| iPhone baseline | iPhone 12 class | Balanced 60 FPS | Not started |
| Newer iPhone | owner-nominated model | High/Balanced 60 FPS | Not started |
| Tablet/aspect ratios | conditional tablet support | safe-area/layout | Not started |

M0: install/launch offline; Title; touch/keyboard/controller menu; OS background,
screen lock, resume confirmation, back/cancel and no latched input.
M1: physical 48 dp-equivalent targets, steering/pedal independence and handling rig.
M2: 30-minute full loop, save/kill/reload, zero-resource rescue, short thermal sample.
M6: three 20-minute routes (forest rain, city dusk, wetlands). Record commit,
editor, package hash, artifact, model, OS, temperature/room, quality, median/P95/P99,
memory/peaks, loading spikes, thermal/battery observations. Balanced target: 95%
frames <=16.67 ms after warmup, no recurring >50 ms stalls, no sustained <50 FPS.
If not met, optimize or declare 30 FPS. Never infer performance from settings.

Also test low storage, airplane mode, Bluetooth audio, battery saver, denied photo
export, cutouts, 150% UI scale/30% text expansion, and controller disconnect.
