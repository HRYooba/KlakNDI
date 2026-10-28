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
  (a multiple of 4 with alpha when sending). The height has no restriction.
  The receiver decodes frames with alpha (UYVA) of any even width and frames
  with padded rows.
- Audio sending: the NDI Sender can send audio samples, and the NDI Audio
  Capture component forwards the audio passing through its GameObject.
- Audio receiving: the NDI Receiver can play received audio through an
  AudioSource on the same GameObject.

[KlakNDI]: https://github.com/keijiro/KlakNDI

To install this fork, add the following line to the `dependencies` section of
`Packages/manifest.json` (replace the tag with the version you need):

```
"jp.keijiro.klak.ndi": "https://github.com/HRYooba/KlakNDI.git?path=Packages/jp.keijiro.klak.ndi#v2.2.0-hryooba.8"
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
Camera/Texture/Audio Only capture method).

**Keep Alpha** - Enable this checkbox to make the stream contain the alpha
channel. You can disable it to reduce the bandwidth.

**Capture Method** - Specify how to capture the video source from the following
options:

  - Game View - The sender captures frames from the Game View.
  - Camera - The sender captures frames from a given camera. This method only
    supports URP and HDRP.
  - Texture - The sender captures frames from a texture asset. You can also use
    a render texture with this option.
  - Audio Only - The sender sends no video. The NDI source carries only the
    audio passed to `SendAudio`, and it appears in Play Mode.

You can attach metadata using the C# `.metadata` property.

The sender sends the whole frame, with the width rounded down to a multiple of
2 (a multiple of 4 with alpha); the columns beyond it on the right edge aren't
sent. To send a part of a texture, copy that part into a texture of its own
and send it.

You can send audio samples with the C# `SendAudio` method. It can be called from
any thread, including the audio thread. With a video capture method, the audio
rides on the NDI source that the video capture creates, so samples are dropped
until the first video frame is captured. Use the Audio Only capture method to
send audio without video. The **NDI Audio Capture** component
(`NdiAudioCapture`) does this for you: attach it to the GameObject with the
AudioListener to send the final mix, or to a GameObject with an AudioSource to
send that source only.

NDI Receiver Component
----------------------

![recv](https://user-images.githubusercontent.com/343936/134309054-8c25ed46-263c-4041-b331-aefc3e0e6107.png)

The **NDI Receiver** component (`NdiReceiver`) receives a video stream and
feeds it to a renderer object or a render texture asset.

**NDI Name** - Specify the name of the NDI source. You can edit the text field
or use the selector to choose a name from currently available NDI sources. A
name without parentheses also matches the stream name part of
"MACHINE (STREAM)", so the receiver finds the stream on any machine. An exact
match takes precedence.

**Extra IPs** - Comma-separated addresses of machines to query directly when
multicast discovery doesn't reach the senders (the same as the Extra IPs
setting of NDI Access Manager). The sources are found by a single finder shared
by all receivers and `NdiFinder`, which queries the addresses of all the
enabled receivers together with the static `NdiReceiver.extraIPs`.

**Target Texture** - The receiver copies the received frames into this render
texture asset.

**Target Renderer** - The receiver overrides a texture property of the given
renderer.

You can extract metadata using the C# `.metadata` property.

On every update, the receiver takes all the frames that have arrived and
decodes only the newest video frame. The C# `.decodedFrameCount` property counts
the decoded frames, so comparing it between updates tells whether the texture
was renewed. The `.videoFrameCaptured` event is invoked on the main thread for
every captured video frame, including the ones skipped in favor of a newer
frame. The frame data is valid only during the call.

Enable **Receive Audio** and add an AudioSource to the same GameObject to play
the received audio. The received audio is mixed into the AudioSource output, and
the AudioSource is started automatically in Play Mode. The received audio is
taken from the queue in `Update`, so a main thread stall longer than the
buffered audio (from 50 ms up to 200 ms) interrupts the audio.

The receiver decodes UYVY and UYVA (with alpha) frames of any even width,
including frames with padded rows. Frames it can't decode (an odd width, or a
line stride that isn't a multiple of 4 or is shorter than width x 2) are
skipped with a warning shown once per frame layout. The `.rejectedFrameCount`
property counts them.

While the source isn't found, the receiver tries to find it once a second.
The time spent in `Update` is recorded under the `NdiReceiver.Update` profiler
marker.

Tips for Scripting
------------------

You can enumerate currently available NDI sources using the NDI Finder class
(`NdiFinder`). See the [Source Selector] example for usage.

When multicast discovery doesn't reach the senders, set
`NdiReceiver.extraIPs = "192.168.0.10,192.168.0.11"` to query those machines
directly (the same as the Extra IPs setting of NDI Access Manager), or set the
`.receiverExtraIPs` property (**Extra IPs**) of a receiver. The shared finder
queries the static addresses and the ones of all the enabled receivers. When
the combined addresses change (for example, a receiver with Extra IPs is
disabled), the finder is recreated the next time it's used, and the receivers
not connected yet wait for the discovery again (a few seconds). Connected
receivers are not affected. To use a separate finder,
create one with `Interop.Find.Create("192.168.0.10,192.168.0.11")`.

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
