# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [2.2.0-hryooba.8] - 2026-10-28

### Removed

- NDI Sender: Source Rect (`sourceRect`) and `sentTextureRegion`. The Texture
  capture method sends the whole source texture again, as the original package
  does. To send a part of a texture, copy that part into a texture of its own.

## [2.2.0-hryooba.7] - 2026-10-28

### Added

- NDI Receiver: Extra IPs property (`receiverExtraIPs`) editable in the
  inspector. The shared finder queries the addresses of all the enabled
  receivers together with the static `extraIPs`.
- NDI Receiver: `rejectedFrameCount` property counting the frames the decoder
  rejected.
- NDI Receiver: `NdiReceiver.Update` profiler marker.

### Changed

- NDI Receiver: A source name without parentheses also matches the stream name
  part of "MACHINE (STREAM)". An exact match takes precedence, and among
  several machines the first name in ordinal order is chosen (with a warning).
- NDI Receiver: The decoder accepts frames with alpha (UYVA) of any even width
  (it required a multiple of 4) and frames with padded rows (it required a
  line stride of width x 2).
- NDI Receiver: While the source isn't found, the receiver tries to find it
  once a second instead of on every update.
- NDI Receiver: The warning about an undecodable frame is shown once per frame
  layout instead of for every frame.

## [2.2.0-hryooba.6] - 2026-10-28

### Added

- NDI Receiver: `videoFrameCaptured` event, invoked for every captured video
  frame (including the ones superseded within the same update) before it's
  released.
- NDI Receiver: `decodedFrameCount` property counting the frames decoded into
  the texture.

### Changed

- NDI Receiver: The receiver drains the frame queue on every update and decodes
  only the newest video frame, also when audio receiving is disabled. It no
  longer falls behind the source when updated less often than the source.

## [2.2.0-hryooba.5] - 2026-10-28

### Added

- NDI Receiver: static `extraIPs` property, shared by all receivers and
  `NdiFinder`, for querying senders at given addresses directly.

## [2.2.0-hryooba.4] - 2026-10-28

### Added

- Interop: `Find.Create(string extraIPs)` for querying senders at given
  addresses directly when multicast discovery doesn't reach them.

## [2.2.0-hryooba.3] - 2026-10-27

### Added

- NDI Sender: Audio Only capture method for sending audio without video.

## [2.2.0-hryooba.2] - 2026-10-26

### Added

- NDI Sender: `sentTextureRegion` property returning the texture region that
  is actually sent.

## [2.2.0-hryooba.1] - 2026-10-26

### Added

- NDI Sender: Source Rect for sending a region of the source texture.
- NDI Sender: `SendAudio` for sending audio samples from any thread.
- NDI Audio Capture component that forwards the audio passing through its
  GameObject to an NDI Sender.
- NDI Receiver: Receive Audio option for mixing received audio into an
  AudioSource on the same GameObject.

### Changed

- Frames no longer have to be multiples of 16x8. The width only has to be a
  multiple of 2 (a multiple of 4 with alpha).
- Video frames are sent with synthesized timecodes so that NDI can align them
  with audio.
- `NdiSender.ndiName` returns the serialized name before the component
  initializes.
- The NDI Receiver skips frames it can't decode (odd widths, unaligned widths
  with alpha, or padded rows) instead of decoding them with broken indices.

## [2.1.6] - 2025-12-07

### Fixed

- Replaced obsolete shader property utilities with Unity 6.3-compatible API
  usage.

## [2.1.5] - 2025-09-16

### Changed

- Updated NDI SDK binaries to v6.2.1.0.
- Refreshed included license files.

## [2.1.4] - 2025-02-07

### Changed

- Updated NDI SDK binaries to v6.1.1.
- Updated installation documentation.
