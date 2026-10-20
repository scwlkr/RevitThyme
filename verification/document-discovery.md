# Session document discovery

## Installed baseline — 2026-10-20

The installed `0.10.0-lifecycle.3` host (source `a6b720c3c997cf77a268b78db0b04b112d87925d`, protocol 12 / 31 tools) reproduced a targeting gap in normal Revit 2027.3. One official-SDK external MCP client opened two exact disposable RVT copies in the background. A fresh independent client still saw Home with no active document identity, and its tool inventory offered no document enumeration. Native open receipts proved both distinct document IDs existed; the fresh caller could not discover them without prior path knowledge.

The first harness attempt used incompatible sharing while hashing an open RVT. It closed normally and independently verified unchanged source/A/B bytes. The corrected harness used shared read access and ran in fresh copies: PID 21360/start 639280981455337891/session dc671823-c673-43a0-b940-19843978344b. It reproduced the gap, activated exact A and explicitly closed B through MCP, verified all file hashes unchanged, and closed the owned process normally. No trusted C#, test observer registration or UI lifecycle fallback was used. Private transcripts, before-state and cleanup evidence remain under ignored `artifacts/document-discovery`.

## Candidate contract

`ListDocuments` uses the existing native queue and API callback. Core owns captured-target validation, sorted session-native identities, bounded pages and a fingerprint over the native session plus every visible returned record. Native enumeration uses [Application.Documents](https://help.autodesk.com/cloudhelp/2026/ENU/Revit-API-MainReference/files/html/8a32ffe0-7e80-e85c-78aa-1af48ace4d74.htm). There is no model transaction, document activation, disk hashing, save or new scheduler. Filtering fixture-bound sessions precedes paging and hashing.

Continuations require the original inventory token and current capture. Background open/close or any returned metadata change rejects continuation even where the active context/revision stays unchanged. Existing document-open snapshot equality and host revision behavior are preserved. Recovered/replayed read outcomes remain point-in-time evidence; fresh inspection requires a new request. Discovery flags do not bypass lifecycle restrictions.

The unreleased `0.10.0-lifecycle.4` candidate uses protocol 13 / 32 tools. Matching-package actual-Revit qualification is **outstanding**. Portable checks do not establish native behavior. Last-active close to Home, broad real-project-warning coverage and bounded model promotion remain separate gaps; >512 changed-ID work is unchanged. No release, website deployment or promotional posts. Physical TimberFold fabrication remains outstanding.
