# Plant compile attachment index

The runner uses seed 8675309, instance 101, mature age 1 and design vigour 0.5 for each of the 125 content graphs. For each LOD 0/1/2 it serializes every float of wood positions, normals, colors, UV and UV2, every index, every instance field, and the same mesh channels for each built shape. `baseline.json` and `candidate.json` contain SHA-256 signatures and counts. All 375 entries matched exactly in this run. This is a digital geometry equivalence check, not a visual or GPU benchmark.

The frozen baseline simulation DLL SHA-256 is A080990AA310080D5231EBA70398F6CB1A6088CA533CF54D528D5797709185D9. Candidate DLL SHA-256 is 8CCEB0B73893EFF46A94AB90324E1A7D21E9385666A3A55BE152576AE0B0679D. Builds used separate `--artifacts-path` locations and `-p:SimDll=<absolute path>`. Verify the copied DLL hash in the runner output directory before executing: MSBuild reused the baseline copy once even with the candidate hint path, so the candidate DLL was explicitly copied over it before the reported run.

LOD 0 compile means over 10 iterations after three warmups, milliseconds and bytes allocated per call, single run per binary; CPU timings are approximate and subject to system load:

| Graph | Organs | Baseline ms | Candidate ms | Baseline bytes | Candidate bytes |
|---|---:|---:|---:|---:|---:|
| fenforge | 6334 | 69.166 | 17.381 | 21366252 | 21416020 |
| moonbark | 4221 | 34.349 | 12.228 | 13488593 | 13522810 |
| mirespire | 1506 | 3.410 | 2.962 | 4380324 | 4392420 |
| shadewrap | 137 | 0.302 | 0.268 | 432252 | 433404 |
| bitterbine | 780 | 2.646 | 1.338 | 1860420 | 1866708 |
| trumpetbind | 557 | 1.522 | 0.947 | 1462740 | 1467252 |

Runner measurements include the complete compile, excluding graph creation and shape mesh creation. The serialized checks include shape meshes. No LOD or foliage density changes were made.
