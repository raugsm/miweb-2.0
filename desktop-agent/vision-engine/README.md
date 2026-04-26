# Vision Engine

Final owner: C#/.NET.

Purpose:

- capture the desktop live without visible shells;
- detect changed regions and windows;
- store raw frames only as temporary local evidence;
- keep raw frames under `D:\AriadGSM\vision-buffer` when available;
- emit `vision_event` contracts;
- never upload raw frames to the cloud by default.

Python `eyes-stream.py` remains the temporary prototype until this engine is replaced by .NET.

