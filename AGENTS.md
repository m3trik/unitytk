# unitytk

<!-- packs (generated: edit the packs, then sync) -->
Shared rules (packs; Claude Code loads them, other agents read them first): [csharp](.claude/rules/csharp.md) · [principles](.agents/packs/principles.md) · [python](.agents/packs/python.md)
<!-- /packs -->

**Role**: The Unity package (`com.m3trik.unitytk/`, the product) and the Python automation beside it (`unitytk/`: the agent host, the project builder, the compile check).

**Nav**: [← root](../AGENTS.md) · [docs](docs/README.md) · **Deps**: [pythontk](../pythontk/AGENTS.md) (the automation; the package needs only Unity) · **Used by**: Unity projects, through the Package Manager; no ecosystem package imports it

## API surface

Searched live (`api.search`; MCP `api_search`), never a committed file — rules in [root](../AGENTS.md). Upstream: [pythontk](../pythontk/AGENTS.md). Nothing in the ecosystem imports unitytk: producers hand it a delivery (an FBX and its records, the contract in pythontk's `SceneRecords`), and the package reads only that.

## The Unity package

Its map is [com.m3trik.unitytk/AGENTS.md](com.m3trik.unitytk/AGENTS.md), in the folder that ships (`com.m3trik.unitytk/`): the parts, where code goes, the never list and the working loop. Read it before touching any C#.

## Run tests

```powershell
& python unitytk\test\run_tests.py test_unity_host -k install    # pointed: modules, -k
& python unitytk\test\run_tests.py --all                  # full: review / release
& python unitytk\test\run_tests.py --all --strict-env     # env-gated contract must run
& python unitytk\test\run_tests.py --edit-mode            # the package's Edit Mode tests (ProjectBuilder.test)
```

What a change reaches: `python m3trik/scripts/affected_tests.py <files> --run` (root Testing).

**The release receipt**: `run_tests.py --all --strict-env --post-status`, on a clean tree, runs the full suite, then the Edit Mode tests on URP and Built-in (`--all` alone starts no editor), and posts the `unity` commit status on HEAD when all passed (`UnityReceipt`); branch protection requires it before main moves, because CI has no Unity or licence.

The integration modules gate on Unity or fixtures: those skips are marked (`base_test.EnvSkip`) and counted on a `SKIPPED (env): N` line, which prints `[PARTIAL]` and withholds the badge (`--strict-env` exits 1). CI runs `--all --no-badge` without `--strict-env`, so **a green `Tests` check does not mean the env-gated contract ran**: run `--strict-env` locally with the assets staged. Editors come from Unity's CLI (`unitytk.UnityCli`); a run's owned project is its own (`TempProjects.owned`), and a run's projects leave the Hub's list with their folders; `UNITYTK_PRESERVE=1` keeps a run's project. No test launches Maya or Blender: a producer's half of the hand-off is the workspace's end-to-end suite.

**Integration assets** are too large to check in and never carry a studio path or client name: the FBX replay suites resolve them from `UNITYTK_TEST_ASSETS` (`base_test.asset_path`; unset = they env-skip), laid out as `fixture_keyed_visibility.fbx`; no other names or old env vars are aliased. `ARTICULATED_RIG_LIVE_FBX` = one real DCC export of an articulated rig for `test_articulated_rig_runtime`'s live half.

See [CHANGELOG.md](CHANGELOG.md) for history.
