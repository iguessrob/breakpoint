# Areal fonts

Breakpoint uses ABC Areal for app text and its tray menu. Windows symbol fonts supply control icons.

Download the desktop package from https://abcdinamo.com/free/areal and accept the publisher's terms. Copy these original files into src/Breakpoint.Desktop/Assets/Fonts/:

- ABCAreal-Regular.ttf
- ABCAreal-Medium.ttf
- ABCAreal-Bold.ttf
- Dinamo-Licensing-Terms.pdf

The build embeds the TTF files in the desktop executable. The tray menu loads them privately for the process; the app does not install fonts into Windows. Release packages include the supplied licensing PDF.

The font files and licensing PDF are excluded from Git. Dinamo's terms restrict publishing font files in public repositories. Keep the publisher's terms with any licensed distribution of the app.
