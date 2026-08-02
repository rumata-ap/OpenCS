# PlanarConnection Slice 6 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpawers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Добавить solver-независимый контракт связи двух `PlanarRegion`, передать её локусы в два независимых Gmsh-run и получить проверяемый `PlanarConnectionMeshMapping` с SQLite round-trip.

**Architecture:** `CScore.Planar` владеет source contract, геометрической валидацией, fingerprints и mapping snapshots. `OpenCS.Gmsh` добавляет тонкий orchestration, который строит request-local curve constraints и последовательно вызывает существующий `IPlanarMesher` для каждой стороны. `DatabaseService` сохраняет source connections и mappings; `OpenCS.OpenSees.CScore`, `CSfea.CScore` и UI не изменяются.

**Tech Stack:** .NET 9, C#, `CScore`, `OpenCS.Gmsh`, raw SQLite через `Microsoft.Data.Sqlite`, xUnit, установленный `C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe`.

---

## Task 1: Domain Contract, Validation, and Fingerprint

**Files:**
- Create: `CScore/Planar/PlanarConnection.cs`
- Create: `CScore/Planar/PlanarConnectionValidator.cs`
- Create: `CScore/Planar/PlanarConnectionFingerprint.cs`
- Create: `CScore.Tests/Planar/PlanarConnectionTests.cs`

- [ ] **Step 1: Write failing domain tests**

  Add xUnit tests in `CScore.Tests/Planar/PlanarConnectionTests.cs` with local helpers for two square regions and a connection whose plate locus is `[new(2, 0), new(2, 4)]`.

  Required cases:

  ```csharp
  [Fact]
  public void Validate_RejectsUnsavedSameAndUnknownRegions()
  {
      var connection = new PlanarConnection
      {
          Id = 0,
          SideA = new ConnectionLocus(10, [new(1, 1), new(1, 3)]),
          SideB = new ConnectionLocus(10, [new(1, 1), new(1, 3)])
      };

      var diagnostics = PlanarConnectionValidator.Validate(
          connection,
          new Dictionary<int, PlanarRegion> { [10] = Region(10) });

      Assert.Contains(diagnostics, item => item.Code == "planar_connection_id_invalid");
      Assert.Contains(diagnostics, item => item.Code == "planar_connection_same_region");
  }
  ```

  Also test unknown region IDs, fewer than two points, non-finite points, zero-length
  segments, self-intersecting polylines, a locus outside/through a hole, and a second
  spatial locus whose transformed endpoints do not match.

  Add a positive test with an inclined `Frame3D` on side B. The transformed local curves
  must match in 3D even when side B points are reversed. Add fingerprint tests proving
  identical contracts produce identical SHA-256 values and changing mode, tolerance,
  region IDs, or points changes the value.

- [ ] **Step 2: Run the focused tests and confirm they fail**

  Run:

  ```powershell
  dotnet test CScore.Tests/CScore.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarConnection
  ```

  Expected: compilation failure because the connection domain types do not exist.

- [ ] **Step 3: Implement the minimal source contract**

  In `PlanarConnection.cs` add Russian XML documentation and:

  - `PlanarConnectionMeshMode` with `ConformingPartition`, `EmbeddedLocus`, `IndependentMpc`;
  - `PlanarConnectionOrientation` with `Forward`, `Reverse`;
  - `ConnectionLocus` containing `int RegionId`, `IReadOnlyList<PlanarPoint2D> Points`, and
    optional `Tag`;
  - `PlanarConnection` containing positive integer `Id`, `Tag`, `ConnectionLocus SideA`,
    `ConnectionLocus SideB`, `MeshMode`, and `MatchingToleranceM` defaulting to `1e-8`;
  - `PlanarConnectionGraph` containing a read-only connection list and a validation method
    that detects duplicate IDs and duplicate `(region pair, spatial locus)` entries.

  Keep source objects independent of live `PlanarRegion` references. A connection with
  `Id == 0` is invalid and cannot produce request-local IDs.

- [ ] **Step 4: Implement validation and canonical fingerprint**

  In `PlanarConnectionValidator.cs` expose:

  ```csharp
  public static IReadOnlyList<FemValidationDiagnostic> Validate(
      PlanarConnection connection,
      IReadOnlyDictionary<int, PlanarRegion> regions);
  ```

  Validate positive ID, distinct existing regions, positive finite tolerance, finite
  open polylines, non-zero segments, self-intersection, and host/hole membership. Transform
  every local point using `Frame3D`; accept direct or reversed side-B orientation only when
  endpoint, total-length, and symmetric vertex-to-polyline distances are within tolerance.
  Reuse the same host rules as `PlanarConstraintValidator` through a curve derived from
  each locus, rather than duplicating hole geometry rules in later Gmsh code.

  In `PlanarConnectionFingerprint.cs` serialize a canonical invariant-culture sequence of
  ID, both region IDs, mesh mode, tolerance, tags, and all local points in side order, then
  return lowercase SHA-256. Do not include Gmsh version here; that remains in
  `PlanarMeshFingerprint`.

- [ ] **Step 5: Run the focused tests and confirm they pass**

  Run the same focused command from Step 2.

  Expected: all new `PlanarConnection` tests pass and the existing `CScore.Tests` suite is
  unchanged.

- [ ] **Step 6: Commit the domain slice**

  ```powershell
  git add CScore/Planar/PlanarConnection.cs CScore/Planar/PlanarConnectionValidator.cs CScore/Planar/PlanarConnectionFingerprint.cs CScore.Tests/Planar/PlanarConnectionTests.cs
  git commit -m "feat(planar): add connection domain contract"
  ```

## Task 2: Snapshot Mapping and Mode Semantics

**Files:**
- Create: `CScore/Planar/PlanarConnectionMapping.cs`
- Create: `CScore/Planar/PlanarConnectionMapper.cs`
- Create: `CScore.Tests/Planar/PlanarConnectionMapperTests.cs`

- [ ] **Step 1: Write failing mapping tests**

  Build synthetic snapshots with nodes on the same global line and a
  `PlanarConstraintMeshMapping` whose ID is `connection:7:region:10` or
  `connection:7:region:20`. Use `OrderedCurveEdges` to describe forward and reverse chains.

  Cover:

  - `EmbeddedLocus` accepts different chain partitions and records side node parameters;
  - `IndependentMpc` records the same independent chains without equations or solver tags;
  - `ConformingPartition` creates exact pairs for equal chains;
  - reverse side orientation is normalized to `Reverse` and exact pairs use canonical order;
  - conforming cardinality/coordinate mismatch is blocking;
  - missing/duplicate constraint mapping, unknown chain node, branch, cycle, and
    non-calculable snapshot are blocking;
  - changed snapshot fingerprints are reported as stale.

- [ ] **Step 2: Run the focused tests and confirm they fail**

  ```powershell
  dotnet test CScore.Tests/CScore.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarConnectionMapper
  ```

  Expected: compilation failure because mapping types and mapper do not exist.

- [ ] **Step 3: Add mapping records and result contract**

  In `PlanarConnectionMapping.cs` add documented types:

  - `PlanarConnectionMeshNode(int NodeIndex, PlanarVector3 Position, double S)`;
  - `PlanarConnectionNodePair(int SideANodeIndex, int SideBNodeIndex, double DistanceM)`;
  - `PlanarConnectionSideMapping` with region ID, constraint ID, orientation, ordered node
    indices, ordered edges, and `PlanarConnectionMeshNode` values;
  - `PlanarConnectionMeshMapping` with connection ID, connection fingerprint, mode, both snapshot IDs/fingerprints,
    side mappings, exact pairs, diagnostics, and `IsCalculable`;
  - `PlanarConnectionMappingResult` with nullable mapping, diagnostics, and `IsCalculable`.

- [ ] **Step 4: Implement the mapper without nearest-node fallback**

  In `PlanarConnectionMapper.cs` expose:

  ```csharp
  public static PlanarConnectionMappingResult Map(
      PlanarConnection connection,
      PlanarRegion regionA,
      PlanarMeshSnapshot sideA,
      PlanarRegion regionB,
      PlanarMeshSnapshot sideB);
  ```

  Validate both regions/snapshots and locate exactly one expected constraint mapping. Transform
  the source locus through `regionA.Frame`/`regionB.Frame`, then walk each mapping's ordered
  edges into a non-branching chain, verify every index, and use the first/last global node
  coordinates to choose `Forward` or `Reverse`. Compute cumulative length and normalized `S`;
  never search for a nearby mesh node.

  Compare fingerprints against `PlanarConnectionFingerprint.Compute(connection)` and the
  snapshot input fingerprints captured in the mapping contract. Apply mode rules: exact
  equal cardinality and coordinate pairs only for `ConformingPartition`; independent chains
  for the other two modes. Return a blocking result instead of a partially calculable
  mapping for any diagnostic with `IsError`.

  Also expose `ValidateCurrent(connection, regionA, mapping, sideA, regionB, sideB)`. It compares the stored
  connection/mode/region IDs and both stored snapshot fingerprints with the current inputs
  and returns `planar_connection_fingerprint_stale` or the more specific blocking diagnostic
  instead of silently reusing an old mapping.

- [ ] **Step 5: Run mapping tests and the full CScore project tests**

  ```powershell
  dotnet test CScore.Tests/CScore.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarConnectionMapper
  dotnet test CScore.Tests/CScore.Tests.csproj --no-restore
  ```

  Expected: focused tests and the complete suite pass; the pre-existing single skipped
  LIRA COM test remains skipped.

- [ ] **Step 6: Commit the mapping slice**

  ```powershell
  git add CScore/Planar/PlanarConnectionMapping.cs CScore/Planar/PlanarConnectionMapper.cs CScore.Tests/Planar/PlanarConnectionMapperTests.cs
  git commit -m "feat(planar): map independent connection meshes"
  ```

## Task 3: Two-Region Gmsh Orchestration

**Files:**
- Create: `OpenCS.Gmsh/PlanarConnectionMeshingWorkflow.cs`
- Create: `OpenCS.Gmsh.Tests/PlanarConnectionMeshingTests.cs`
- Modify: `OpenCS.Gmsh/OpenCS.Gmsh.csproj` only if a new project reference is required; the
  intended implementation uses existing `CScore` references and should not require one.

- [ ] **Step 1: Write failing workflow tests**

  Add a capturing fake `IPlanarMesher` that records every `PlanarMeshingRequest` and returns
  supplied snapshots. Test that a valid saved connection causes exactly two requests with
  IDs `connection:7:region:10` and `connection:7:region:20`, curve geometry in each local
  frame, the correct mesh facet per mode, and a composite source fingerprint. Assert both
  source regions remain unchanged. Test invalid graph preflight causes zero mesher calls.

- [ ] **Step 2: Run the focused workflow tests and confirm they fail**

  ```powershell
  dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarConnectionMeshing
  ```

  Expected: compilation failure because the workflow does not exist.

- [ ] **Step 3: Implement request-local workflow**

  In `PlanarConnectionMeshingWorkflow.cs` add:

  ```csharp
  public sealed record PlanarConnectionMeshingResult(
      PlanarMeshSnapshot? SideA,
      PlanarMeshSnapshot? SideB,
      PlanarConnectionMeshMapping? Mapping,
      IReadOnlyList<FemValidationDiagnostic> Diagnostics);

  public sealed class PlanarConnectionMeshingWorkflow
  {
      public Task<PlanarConnectionMeshingResult> BuildAsync(
          PlanarConnection connection,
          PlanarRegion sideA,
          PlanarMeshSettings settingsA,
          PlanarRegion sideB,
          PlanarMeshSettings settingsB,
          IReadOnlyList<PlanarConstraintObject>? additionalConstraintsA = null,
          IReadOnlyList<PlanarConstraintObject>? additionalConstraintsB = null,
          string? sourceFingerprintA = null,
          string? sourceFingerprintB = null,
          CancellationToken cancellationToken = default);
  }
  ```

  Validate the connection against the two regions before invoking the mesher. Build one
  derived curve constraint per side, merge it with region constraints and additional
  FEM-derived constraints without mutating either region, and combine source fingerprints
  deterministically. Call the injected `IPlanarMesher` sequentially so diagnostics and
  cancellation are deterministic while artifact directories remain independent. Invoke
  `PlanarConnectionMapper` only after both snapshots return.

- [ ] **Step 4: Run workflow tests and existing Gmsh tests**

  ```powershell
  dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarConnectionMeshing
  dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore
  ```

  Expected: new workflow tests and all previous 24 Gmsh tests pass.

- [ ] **Step 5: Commit the orchestration slice**

  ```powershell
  git add OpenCS.Gmsh/PlanarConnectionMeshingWorkflow.cs OpenCS.Gmsh.Tests/PlanarConnectionMeshingTests.cs
  git commit -m "feat(gmsh): build two planar connection meshes"
  ```

## Task 4: SQLite v49 Persistence

**Files:**
- Modify: `OpenCS/Utilites/DatabaseService.cs:33,59-67,547-580,1119-1192,5348-5675`
- Create: `OpenCS.Gmsh.Tests/PlanarConnectionPersistenceTests.cs`

- [ ] **Step 1: Write failing persistence and migration tests**

  In `PlanarConnectionPersistenceTests.cs`, use the existing temporary database pattern from
  `PlanarMeshPersistenceTests` and add tests for:

  - connection source round-trip after `SavePlanarConnection`/`GetPlanarConnections`;
  - two locus JSON arrays, mode, tolerance, tag and fingerprint round-trip;
  - mapping round-trip with side chains, orientation, `S`, exact pairs, diagnostics and both
    snapshot fingerprints;
  - v48 database migration creates both new tables and stores schema version `49`;
  - deleting a region explicitly removes connections and their mappings despite the service's
    `PRAGMA foreign_keys=OFF` policy.

- [ ] **Step 2: Run the focused persistence tests and confirm they fail**

  ```powershell
  dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarConnectionPersistence
  ```

  Expected: compilation failure because the database methods and schema do not exist.

- [ ] **Step 3: Add schema v49 and idempotent table creation**

  In `DatabaseService.cs`:

  - change `CurrentSchemaVersion` from `48` to `49`;
  - call `EnsurePlanarConnectionTables()` in the constructor after existing planar mesh table
    setup so fresh databases contain the tables;
  - add `if (i == 48) { MigrateV49(); continue; }` to `Migrate()`;
  - implement `MigrateV49()` as idempotent `EnsurePlanarConnectionTables()`;
  - create `planar_connections` with integer autoincrement ID, tag, two region foreign keys,
    locus JSON, mode, tolerance, and fingerprint;
  - create `planar_connection_mappings` with connection/snapshot references, mode,
    fingerprints, mapping JSON, diagnostics JSON and calculable flag, with a unique key on
    `(connection_id, snapshot_a_id, snapshot_b_id)`;
  - use `ON DELETE CASCADE` in the schema but also perform explicit cleanup in
    `DeletePlanarRegion` because the service intentionally disables SQLite foreign keys.

- [ ] **Step 4: Implement parameterized CRUD and atomic mapping persistence**

  Add `AddPlanarConnection`, `UpdatePlanarConnection`, `GetPlanarConnections(schemaId)`,
  `DeletePlanarConnection`, `SavePlanarConnectionMeshMapping`, and
  `GetPlanarConnectionMeshMappings(connectionId)`. Resolve connections by joining either
  region to `fem_schemas` when loading by schema. Serialize all nested records using the
  existing `_jsonSettings`; do not add connection objects to
  `planar_regions.constraint_objects_json`.

  Save mapping and its diagnostics in one transaction. Load exact side snapshot IDs and
  fingerprints so stale detection remains possible after remesh. Delete connection mappings
  before deleting a connection or region.

- [ ] **Step 5: Run persistence tests and the complete Gmsh test project**

  ```powershell
  dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarConnectionPersistence
  dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore
  ```

  Expected: migration, source round-trip, mapping round-trip and all prior tests pass.

- [ ] **Step 6: Commit persistence**

  ```powershell
  git add OpenCS/Utilites/DatabaseService.cs OpenCS.Gmsh.Tests/PlanarConnectionPersistenceTests.cs
  git commit -m "feat(db): persist planar connections and mappings"
  ```

## Task 5: Real Gmsh Integration Fixture

**Files:**
- Modify: `OpenCS.Gmsh.Tests/PlanarConnectionMeshingTests.cs`

- [ ] **Step 1: Add the real-process test fixture**

  Use the fixed executable path from `AGENTS.md` and a unique temporary artifact root. Build:

  - side A as a 4x4 region in `Frame3D.Identity` with local locus `[new(2, 0), new(2, 4)]`;
  - side B as a 4x2 vertical region with `Origin = (2, 0, -1)`,
    `LocalX = (0, 1, 0)`, `LocalY = (0, 0, 1)`, `LocalZ = (1, 0, 0)`, and reversed local
    locus `[new(4, 1), new(0, 1)]`;
  - `EmbeddedLocus` mode so independently generated interface partitions are accepted.

  The global interface is the line `x=2, z=0, y=0..4`; the curve is interior except for
  endpoints, and both Gmsh runs must use MSH 4.1. Assert both snapshots are calculable,
  each contains the deterministic connection constraint mapping, the mapping is calculable,
  side B orientation is `Reverse`, and no Gmsh tag is reused as a cross-region identity.

  Delete the unique artifact directory in `finally`, matching existing real-Gmsh tests.

- [ ] **Step 2: Run the real integration test**

  ```powershell
  dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarConnectionMeshingTests
  ```

  Expected: all real Gmsh connection assertions pass. If Gmsh emits a geometry diagnostic,
  preserve the artifact directory temporarily and fix the generated locus/fixture rather
  than weakening `IsCalculable` or using nearest-node matching.

- [ ] **Step 3: Commit the integration fixture**

  ```powershell
  git add OpenCS.Gmsh.Tests/PlanarConnectionMeshingTests.cs
  git commit -m "test(gmsh): cover two-region connection mapping"
  ```

## Task 6: Final Verification and Documentation Consistency

**Files:**
- Modify: no production files unless verification exposes a defect;
- Review: `docs/superpowers/specs/2026-08-02-gmsh-planar-connections-slice6-design.md`;
- Review: supplied Obsidian roadmap notes for reporting only, not repository source.

- [ ] **Step 1: Run focused suites after all implementation commits**

  ```powershell
  dotnet test CScore.Tests/CScore.Tests.csproj --no-restore
  dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore
  ```

  Expected: all new and existing tests pass; CScore retains its one known skipped test.

- [ ] **Step 2: Run solution build and the existing OpenSees regression suite**

  ```powershell
  dotnet build OpenCS.sln --no-restore
  dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --no-build --no-restore
  ```

  Expected: solution build has zero errors and only the known two missing `OpenCS.Core.UI`
  warnings. The OpenSees suite may reproduce the pre-existing SQLite cleanup race; record
  its exact count and failing test names separately, and do not attribute it to this slice.

- [ ] **Step 3: Review the diff and working tree**

  ```powershell
  git diff --check
  git status -sb
  git log --oneline -10
  ```

  Confirm only intended source, test, spec, and plan files changed; no generated database,
  Gmsh artifacts, `bin/`, or `obj/` files are staged.

- [ ] **Step 4: Commit any final test/documentation fix and report evidence**

  If verification requires a fix, add a focused commit with the relevant test. Otherwise
  leave the implementation commits intact and report the exact commands, counts, known
  baseline race, and the final branch/status. Do not claim the OpenSees suite is green if
  the cleanup race recurs.
