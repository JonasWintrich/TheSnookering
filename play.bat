@echo off
rem Launch Snookering (gray-box build)
cd /d "%~dp0"
start "" "tools\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe" --path game
