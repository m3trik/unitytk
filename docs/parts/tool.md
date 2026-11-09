# A tool

**Belongs:** the menus and windows over a feature's actions, in `Tools/<Feature>/` (its own editor assembly, `autoReferenced: false`, referencing the kernel and the features it serves); view state only. It ships in the package beside the feature it is named for. **Does not:** change a project itself: every change is a feature's action.

**Add one:** a menu item is one line: `[MenuItem("Tools/unitytk/<Family>/<Item>")] static void Item() => OpContext.RunMenu("<Item>", ctx => <Feature>s.<Action>(ctx: ctx));`. `RunMenu` gives one undo step, a progress bar with Cancel, and the issues in the Console; say what the run did with `ctx.Log`. A menu path is published: keep it when code moves.

**Checked by:** `check_csharp.py` (no direct `Undo`, progress bar or `Debug.Log`); `check_layers.py`.
