# A contract

**Belongs:** an interface two features meet through, in `Contracts/` (a runtime one, read off components with `GetComponent<IThing>()`) or `Contracts/Editor/` (an editor one, a provider served by `Provided.One<IThing>()`), each with its `Pure*` stand-in beside it. **Does not:** hold behaviour of its own, or name a feature. Changing one is a foundation change: stop and ask.

**Add one:**

1. Write the interface and its stand-in in one file (`IRecordSource.cs` holds `PureRecordSource`). An editor stand-in carries `[Provides(typeof(IThing), StandIn = true)]`; a real provider `[Provides(typeof(IThing))]`, and replaces it.
2. A contract read during an import declares its rank, `[ImportRank(n)]`, so its provider imports first (`ImportOrder.Provider` / `.Reader`).
3. A need is a parameter of the contract's type that defaults to its provider: `if (records == null) records = Provided.One<IRecordSource>();`. A test passes the stand-in.
4. Two real providers are an error the project doctor names; a need that takes them all asks `Provided.All<IThing>()`.

**Checked by:** the compiler (features reach each other only through it); `Provided.Problems()` in `unity.project.doctor`; one fixture run against the stand-in and each provider.
