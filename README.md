# Trackarr

Trackarr is a very early self-hosted companion app for Radarr and Sonarr.

The goal is to make it easier to see what is happening with your media setup, especially around downloads, queue activity, missing media, failed items, and things that need attention.

This project is still in a rough testing stage. It is not ready for normal use yet.

## Current status

Trackarr is currently in early alpha development.

A lot of the core pieces are working, but the app is still being tested, cleaned up, and prepared for a real release.

Right now, the focus is not adding more features. The focus is making the current app more stable.

## What Trackarr is trying to do

Trackarr is being built to help answer questions like:

* What is currently downloading?
* What items are stuck?
* What items keep failing?
* What media is missing?
* What has been searched already?
* What needs attention?
* Are Radarr and Sonarr doing what I expect them to do?

The long-term goal is to give a clearer overview of media automation health without needing to jump between multiple apps.

## What works right now

Some of the current work includes:

* Radarr and Sonarr connection support
* Queue tracking
* Missing media tracking
* Download health scoring
* Download attempt history
* Background routines
* Routine run history
* Media detail views
* Basic dashboard pages
* Table-based UI using Bootstrap and Tabler

These areas are still being tested and may change.

## What is not done yet

This is still an early build, so expect unfinished parts.

Known rough areas include:

* Some settings are not editable yet
* Some UI pages still need cleanup
* Some backend paths still need hardening
* Docker deployment is still being tested
* Tests are limited right now
* Documentation is incomplete
* Breaking changes may happen
* Database changes may happen


## Testing focus

Current testing is focused on:

* App startup
* Queue refresh behavior
* Missing media refresh behavior
* Radarr/Sonarr connection handling
* Offline server handling
* Background routine execution
* Database stability
* Docker testing
* Basic UI stability

## Project direction

For now, Trackarr is being treated as an early test build.

The next steps are mostly:

* Fix broken backend behavior
* Clean up rough UI issues
* Improve stability
* Add tests around fragile logic
* Improve error handling
* Make deployment more reliable
* Prepare the project for public feedback

## Contributing

Trackarr is not fully organized for outside contributions yet.

Bug reports, testing feedback, and setup feedback will be the most useful once the first alpha build is available.

Major feature work should wait until the base app is more stable.

## Disclaimer

Trackarr is an independent project.

It is not affiliated with Radarr, Sonarr, or any download client.

This is early alpha software. Use it for testing only. Use at your own risk. The software does interact with live versons of Sonarr and Radarr with no ability to disable that functionality yet. 
