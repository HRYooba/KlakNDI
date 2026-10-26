KlakNDI
=======

![gif](https://i.imgur.com/I1ZMSY8.gif)

**KlakNDI** is a Unity plugin that allows for sending and receiving video
streams between multiple devices using [NDI]®.

[NDI]® (Network Device Interface) is a standard developed by [Vizrt] that
enables applications to deliver video streams over a local area network. Please
refer to [ndi.video][NDI] for more information about the technology.

[NDI]: https://ndi.video/
[Vizrt]: https://www.vizrt.com

About This Fork
---------------

This is a fork of [KlakNDI] with the following additions. It keeps the
original package name, assembly names, and namespaces, so it works as a drop-in
replacement for the original package.

- Frames of arbitrary dimensions: the width only has to be a multiple of 2
  (a multiple of 4 with alpha). The height has no restriction.
- Source region: the NDI Sender can send a rectangular region of a source
  texture (Texture capture method).
- Audio sending: the NDI Sender can send audio samples, and the NDI Audio
  Capture component forwards the audio passing through its GameObject.
- Audio receiving: the NDI Receiver can play received audio through an
  AudioSource on the same GameObject.

[KlakNDI]: https://github.com/keijiro/KlakNDI

To install this fork, add the following line to the `dependencies` section of
`Packages/manifest.json` (replace the tag with the version you need):

```
"jp.keijiro.klak.ndi": "https://github.com/HRYooba/KlakNDI.git?path=Packages/jp.keijiro.klak.ndi#v2.2.0-hryooba.2"
```

System Requirements
-------------------

- Unity 2022.3 or later

Desktop platforms:

- Windows: x64, D3D11/D3D12
- macOS: x64 or arm64 (M1), Metal
- Linux: x64, Vulkan

Mobile platforms:

- iOS: arm64, Metal
- Android: arm64, Vulkan/OpenGL ES 3.x

KlakNDI runs without the NDI SDK on most supported platforms, but the iOS
platform requires the SDK for building with Xcode. Please download and install
the NDI SDK for iOS in advance of building.

KlakNDI requires network permissions on Android. Please follow the instruction
in [Android Support section].

[Android Support section]: README.md#android-support

License
-------

The NDI library files are provided under the terms of the [NDI SDK license].
Please review it before using the package in your project.

[NDI SDK license]: http://ndi.link/ndisdk_license

Known Issues and Limitations
----------------------------

- The width of frame images should be a multiple of 2 (a multiple of 4 with
  alpha).

- Received audio is resampled to the output rate with linear interpolation and
  kept within a small latency window (about 50 to 200 ms). Clock drift between
  the sender and the receiver is absorbed by dropping samples or inserting
  silence, which can be audible.

How To Install
--------------

The KlakNDI package (`jp.keijiro.klak.ndi`) can be installed via the "Keijiro"
scoped registry using Package Manager. To add the registry to your project,
please follow [these instructions].

[these instructions]:
  https://gist.github.com/keijiro/f8c7e8ff29bfe63d86b888901b82644c

NDI Sender Component
--------------------

![send](https://user-images.githubusercontent.com/343936/134309035-aa5be91f-098b-4352-a49f-0c2d4f49f5b0.png)

The **NDI Sender** component (`NdiSender`) sends a video stream from a given
video source.

**NDI Name** - Specify the name of the NDI endpoint (only available in the
Camera/Texture capture method).

**Keep Alpha** - Enable this checkbox to make the stream contain the alpha
channel. You can disable it to reduce the bandwidth.

**Capture Method** - Specify how to capture the video source from the following
options:

  - Game View - The sender captures frames from the Game View.
  - Camera - The sender captures frames from a given camera. This method only
    supports URP and HDRP.
  - Texture - The sender captures frames from a texture asset. You can also use
    a render texture with this option.

You can attach metadata using the C# `.metadata` property.

With the Texture capture method, **Source Rect** selects a region of the source
texture in pixels from the bottom-left corner. Leave it zero-sized to send the
whole texture. The region may extend outside the texture; those pixels are sent
as black. The `sentTextureRegion` property returns the region that is actually
sent (with the width rounded down to a packable value).

You can send audio samples with the C# `SendAudio` method. It can be called from
any thread, including the audio thread. The audio rides on the NDI source that
the video capture creates, so samples are dropped until the first video frame
is captured. The **NDI Audio Capture** component
(`NdiAudioCapture`) does this for you: attach it to the GameObject with the
AudioListener to send the final mix, or to a GameObject with an AudioSource to
send that source only.

NDI Receiver Component
----------------------

![recv](https://user-images.githubusercontent.com/343936/134309054-8c25ed46-263c-4041-b331-aefc3e0e6107.png)

The **NDI Receiver** component (`NdiReceiver`) receives a video stream and
feeds it to a renderer object or a render texture asset.

**NDI Name** - Specify the name of the NDI source. You can edit the text field
or use the selector to choose a name from currently available NDI sources.

**Target Texture** - The receiver copies the received frames into this render
texture asset.

**Target Renderer** - The receiver overrides a texture property of the given
renderer.

You can extract metadata using the C# `.metadata` property.

Enable **Receive Audio** and add an AudioSource to the same GameObject to play
the received audio. The received audio is mixed into the AudioSource output, and
the AudioSource is started automatically in Play Mode. Frames with padded rows
(line stride other than width x 2) are not supported and are skipped.

Tips for Scripting
------------------

You can enumerate currently available NDI sources using the NDI Finder class
(`NdiFinder`). See the [Source Selector] example for usage.

[Source Selector]: URP/Assets/Script/SourceSelector.cs

You can instantiate the NDI Sender/Receiver component from a script but at
the same time, you have to set an NDI Resources asset (`NdiResources.asset`).
See the [Sender Benchmark]/[Receiver Benchmark] examples for details.

[Sender Benchmark]: URP/Assets/Script/SenderBenchmark.cs
[Receiver Benchmark]: URP/Assets/Script/ReceiverBenchmark.cs

Android Support
---------------

KlakNDI requires the following permissions when running on Android:

- `android.permission.INTERNET`
- `android.permission.ACCESS_NETWORK_STATE`
- `android.permission.CHANGE_WIFI_MULTICAST_STATE`

You can add them by [overriding the App Manifest]. Please refer to the
[AndroidManifest file] contained in the URP sample.

[overriding the App Manifest]:
  https://docs.unity3d.com/Manual/overriding-android-manifest.html
[AndroidManifest file]: URP/Assets/Plugins/Android/AndroidManifest.xml
