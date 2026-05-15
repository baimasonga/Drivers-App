# AVDP Driver — Native Android App

This folder wraps the Blazor WASM driver PWA into a **real native Android APK** using [Capacitor](https://capacitorjs.com). The result is an installable app you can sign and distribute via the Play Store, MDM, or sideload directly.

The Capacitor wrapper gives the app:

- A real Android app icon and launch screen
- Access to native Geolocation, Camera, Push Notifications, Network status
- Status bar and splash screen control
- Offline-by-default WebView (no browser chrome)

## Prerequisites

- **Node.js 18+** (you already have v22)
- **Android Studio** with Android SDK 33+ and Build Tools
- **JDK 17** (bundled with Android Studio Iguana+)
- The published Blazor app at `../publish/driver-app/wwwroot`

## One-time setup

```bash
cd mobile/android-wrapper
npm install
# This will pull in @capacitor/core, @capacitor/cli and the plugins
npx cap add android
```

After the first `cap add android`, you'll have an `android/` folder containing a full Gradle / Android Studio project — that **is** the native Android app source.

## Configure the API endpoint

Edit `../../src/AvdpSmartFleet.DriverApp/wwwroot/appsettings.Production.json`:

```json
{ "ApiBaseUrl": "https://avdp.example.org/" }
```

This must point at the **live API server** because the Android app talks to it over HTTPS.

## Build the app

```bash
npm run build
```

This:

1. Publishes the Blazor WASM project in Release mode to `../../publish/driver-app/`
2. Copies the published `wwwroot` contents into `./www`
3. Runs `cap sync` to copy the web assets into the Android project and update plugin native bindings

## Open in Android Studio

```bash
npm run open
```

This opens the generated `android/` project in Android Studio. From there:

- **Build → Build Bundle(s) / APK(s) → Build APK(s)** produces a debug APK at `android/app/build/outputs/apk/debug/app-debug.apk`
- **Build → Generate Signed Bundle / APK** for a release build (you'll need a Java keystore — see Play Console docs)
- **Run** with a connected phone or emulator to test live

## Run on a real Android phone

1. Enable Developer Options + USB Debugging on your phone
2. Connect via USB
3. From the wrapper directory: `npm run android`

This builds, syncs, and installs in one command.

## App identity

- **Application ID**: `sl.avdp.smartfleet.driver`
- **App name**: AVDP Driver
- **Theme color**: AVDP green (#0f3d18)

Change these in `capacitor.config.json` before your first build if you want a different package name.

## Updating the web content

The wrapper is a thin shell. Every time you change the Blazor code:

```bash
npm run build         # republish and sync
```

Then rebuild the APK in Android Studio. No Kotlin/Java changes are needed for normal feature work — only when you add a new Capacitor plugin or need to touch AndroidManifest.xml.

## Permissions baked in

The Capacitor config requests these at install / first use:

- **Location** (for GPS-stamped trip logs and road alerts)
- **Camera** (for odometer + incident photos)
- **Network state** (for offline queue detection)
- **Notifications** (for push alerts when wired up)

## Why Capacitor and not Trusted Web Activity?

TWA requires the PWA to be hosted on a public HTTPS domain with Digital Asset Links verification, which is brittle and ties the app to a single deployment URL. Capacitor bundles the web assets directly into the APK so the app works the moment it's installed, even before it talks to the API server. Better for AVDP's low-connectivity field reality.
