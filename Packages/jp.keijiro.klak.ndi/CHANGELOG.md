# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [2.2.0-hryooba.1] - 2026-10-26

### Added

- NDI Sender: Source Rect for sending a region of the source texture.
- NDI Sender: `SendAudio` for sending audio samples from any thread.
- NDI Audio Capture component that forwards the audio passing through its
  GameObject to an NDI Sender.
- NDI Receiver: Receive Audio option for playing received audio through an
  AudioSource on the same GameObject.

### Changed

- Frames no longer have to be multiples of 16x8. The width only has to be a
  multiple of 2 (a multiple of 4 with alpha).
- Video frames are sent with synthesized timecodes so that NDI can align them
  with audio.
- `NdiSender.ndiName` returns the serialized name before the component
  initializes.

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
