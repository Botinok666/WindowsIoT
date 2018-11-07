# WindowsIoT
Project related to DSI light controlling system based on RPi 3 and custom-built modules with MCUs
Current configuration: Raspberry Pi 3 with 5" chinese LCD with touch screen. Touch input is implemented with input injection library.
External devices are connected through RS485-like bus (UART is used on RPi). Brightness of the screen can be controlled by SW (using PCA9685 PWM controller and MAX44009 lux sensor).
External devices: two lighting controllers for 4 lamp groups each (8 DSI channels and 1 simple channel) and one controller for cooling system in shower.
