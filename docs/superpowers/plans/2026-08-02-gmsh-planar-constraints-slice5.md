# Gmsh PlanarConstraintObject Slice 5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpawers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add persistent point/curve/region constraint loci to one `PlanarRegion`, generate them through deterministic Gmsh MSH 4.1, and persist exact node/edge/element mappings without applying solver constraints.

**Architecture:** Keep `PlanarRegion` as the source of geometry and add serializable `PlanarConstraintObject` values with independent structural and mesh facets. Split the current Gmsh implementation into domain-neutral MSH 4.1 parsing, deterministic `.geo` generation, and constraint mapping; extend `PlanarMeshSnapshot` and SQLite with provenance and mappings while retaining dense snapshot indices for existing shell adapters.

**Tech Stack:** C#/.NET 9, `CScore` domain library, `OpenCS.Gmsh` external-process adapter, raw SQLite through `Microsoft.Data.Sqlite`, xUnit tests, real `C:\Tools\gmsh-4.15.2-Windows64\gmsh.exe` integration tests.

**Specification:** `docs/superpowers/specs/2026-08-02-gmsh-planar-constraints-slice5-design.md`

---

## Research Baseline

- `CScore/Planar/PlanarRegion.cs` owns contours, boundary segments, rebar zones and mesh size; `RecalcFingerprint()` currently hashes contours, frame and boundary segments.
- `CScore/Planar/PlanarMeshSnapshot.cs` currently stores dense nodes/elements and boundary node chains, but no entity or internal-locus mappings.
- `OpenCS.Gmsh/GmshPlanarMesher.cs` currently combines `.geo` generation, MSH 2.2 parsing, boundary mapping and process orchestration; its `BuildAsync()` is the only production meshing entry point.
- `OpenCS.Gmsh.Tests/GmshPlanarMesherTests.cs` runs a real Gmsh executable against a non-convex host with a hole and checks boundary mappings and artifacts.
- `OpenCS.Gmsh.Tests/PlanarMeshPersistenceTests.cs` uses temporary SQLite files and `DatabaseService` to test snapshot and region round-trips.
- `OpenCS/Utilites/DatabaseService.cs` creates final schemas in `EnsureCreated()`, tracks schema version 46, applies migrations through `MigrateV46()`, and keeps derived planar mesh tables separate from `fem_mesh_*`.
- `CScore.Tests` is xUnit for pure domain logic; `OpenCS.Gmsh.Tests` is xUnit for Gmsh/database integration; `CSfea.Tests` is a separate console harness and is not the primary test target for this slice.

## File Map

### Create

- `CScore/Planar/PlanarConstraintObject.cs` — constraint object, geometry kinds, structural and mesh facet contracts.
- `CScore/Planar/PlanarConstraintGeometry.cs` — local point/polyline/polygon values and geometric primitives.
- `CScore/Planar/PlanarConstraintValidator.cs` — host-region, hole, self-intersection, compatibility and conflict diagnostics.
- `CScore/Planar/PlanarMeshEntityProvenance.cs` — logical/entity/physical provenance records.
- `CScore/Planar/PlanarConstraintMeshMapping.cs` — point, edge-chain and region mapping result.
- `CScore.Tests/Planar/PlanarConstraintTests.cs` — domain construction, facet compatibility and validation tests.
- `CScore.Tests/Planar/PlanarConstraintFingerprintTests.cs` — fingerprint invalidation tests.
- `CScore.Tests/Planar/PlanarConstraintMeshMappingTests.cs` — mapping validation tests.
- `OpenCS.Gmsh/Parsing/GmshMsh41Document.cs` — parsed MSH 4.1 sections and raw entity/block records.
- `OpenCS.Gmsh/Parsing/GmshMsh41Reader.cs` — strict ASCII MSH 4.1 reader.
- `OpenCS.Gmsh/Generation/GmshPlanarGeoBuilder.cs` — deterministic host and constraint `.geo` generation.
- `OpenCS.Gmsh/Mapping/PlanarConstraintMeshMapper.cs` — parsed entity/element data to domain mappings.
- `OpenCS.Gmsh.Tests/GmshMsh41ReaderTests.cs` — parser fixtures and malformed-input diagnostics.
- `OpenCS.Gmsh.Tests/PlanarConstraintMeshingTests.cs` — real Gmsh point/curve/region integration scenarios.

### Modify

- `CScore/Planar/PlanarRegion.cs` — add `ConstraintObjects` and include it in fingerprint recalculation.
- `CScore/Planar/PlanarGeometryFingerprint.cs` — canonicalize and hash constraints.
- `CScore/Planar/PlanarMeshFingerprint.cs` — advance mesh contract version and include MSH format/provenance.
- `CScore/Planar/PlanarMeshSnapshot.cs` — add mesh format, entity provenance and constraint mappings.
- `CScore/Planar/PlanarMeshSnapshotValidator.cs` — validate entity and constraint mappings.
- `CScore.Tests/Planar/PlanarGeometryFingerprintTests.cs` — include constraint changes in regression assertions.
- `CScore.Tests/Planar/PlanarMeshFingerprintTests.cs` — cover format and constraint changes.
- `OpenCS.Gmsh/GmshPlanarMesher.cs` — orchestrate validation, new generator, MSH 4.1 reader and mapper; keep process timeout/artifact behavior.
- `OpenCS.Gmsh.Tests/GmshPlanarMesherTests.cs` — assert MSH 4.1 provenance and preserve existing hole/boundary checks.
- `OpenCS.Gmsh.Tests/PlanarMeshPersistenceTests.cs` — round-trip constraints, mappings, format and provenance.
- `OpenCS/Utilites/DatabaseService.cs` — schema v47, region JSON, snapshot metadata and mapping CRUD.

## Task 1: Add the Constraint Domain Contract

**Files:**

- Create: `CScore/Planar/PlanarConstraintGeometry.cs`
- Create: `CScore/Planar/PlanarConstraintObject.cs`
- Create: `CScore/Planar/PlanarConstraintValidator.cs`
- Modify: `CScore/Planar/PlanarRegion.cs`
- Create: `CScore.Tests/Planar/PlanarConstraintTests.cs`

- [ ] **Step 1: Write failing domain tests.** Cover a valid point, open polyline and closed polygon; reject duplicate constraint IDs, non-finite coordinates, degenerate curves/polygons, incompatible geometry/facet pairs, missing master references for `RigidBody`/`PointMpc`/`EmbeddedMember`, and missing DOF masks for `Support`/`Symmetry`.

```csharp
[Fact]
public void Validate_RejectsStructuralFacetWithoutRequiredReference()
{
    var constraint = PlanarConstraintObject.Point(
        "rigid-1", new PlanarPoint2D(0.5, 0.5),
        new PlanarStructuralFacet(PlanarStructuralKind.RigidBody),
        new PlanarMeshFacet(PlanarMeshKind.EmbeddedPoint));

    var diagnostics = PlanarConstraintValidator.Validate(region, [constraint]);

    Assert.Contains(diagnostics, d => d.Code == "planar_constraint_master_reference_missing");
}
```

- [ ] **Step 2: Run the focused test to verify it fails.**

Run: `dotnet test CScore.Tests/CScore.Tests.csproj --filter FullyQualifiedName~PlanarConstraintTests --no-restore`

Expected: compilation failure because the new domain types and validator do not exist.

- [ ] **Step 3: Implement the minimal serializable contract.** Use explicit enum discriminators and records/classes that `System.Text.Json` can round-trip without polymorphic metadata:

```csharp
public enum PlanarConstraintGeometryKind { Point, Curve, Region }
public enum PlanarStructuralKind { None, RigidBody, Tie, EmbeddedMember, PointMpc, Support, Symmetry }
public enum PlanarMeshKind { None, EmbeddedPoint, EmbeddedCurve, EmbeddedRegion, ConformingPartition }

[Flags]
public enum PlanarDofMask
{
    None = 0, UX = 1, UY = 2, UZ = 4, RX = 8, RY = 16, RZ = 32
}

public sealed record PlanarPoint2D(double U, double V);
public sealed record PlanarConstraintGeometry(
    PlanarConstraintGeometryKind Kind,
    IReadOnlyList<PlanarPoint2D> Points);
public sealed record PlanarMasterReference(string Provider, string Key);
public sealed record PlanarStructuralFacet(
    PlanarStructuralKind Kind,
    PlanarMasterReference? MasterReference = null,
    PlanarDofMask DofMask = PlanarDofMask.None,
    Frame3D? Frame = null);
public sealed record PlanarMeshFacet(PlanarMeshKind Kind);
```

`PlanarConstraintObject` must expose `Id`, `Tag`, `Geometry`, `StructuralFacet`, `MeshFacet`, `ToleranceM` (default `1e-9`) and optional source provenance, plus factory methods that create point/curve/region values without duplicating validation rules.

- [ ] **Step 4: Add host-aware validation.** Validate hull/holes with existing topology conventions, allow a curve endpoint on the outer boundary, reject loci inside holes or crossing host boundaries, reject self-intersections and non-finite tolerances, enforce the facet compatibility table, and reject nontrivial curve/region overlap or nesting. Return `FemValidationDiagnostic` codes instead of throwing for user input.

- [ ] **Step 5: Attach constraints to `PlanarRegion`.** Add `List<PlanarConstraintObject> ConstraintObjects { get; set; } = [];` and validate it through `PlanarRegionValidator.Validate()` so meshing and persistence share one source of diagnostics.

- [ ] **Step 6: Run focused domain tests.**

Run: `dotnet test CScore.Tests/CScore.Tests.csproj --filter FullyQualifiedName~PlanarConstraintTests --no-restore`

Expected: all constraint construction and validation tests pass.

- [ ] **Step 7: Commit the domain slice.**

```bash
git add CScore/Planar CScore.Tests/Planar
git commit -m "feat(planar): add constraint object geometry contract"
```

## Task 2: Extend Fingerprints and Snapshot Contracts

**Files:**

- Create: `CScore/Planar/PlanarMeshEntityProvenance.cs`
- Create: `CScore/Planar/PlanarConstraintMeshMapping.cs`
- Modify: `CScore/Planar/PlanarRegion.cs`
- Modify: `CScore/Planar/PlanarGeometryFingerprint.cs`
- Modify: `CScore/Planar/PlanarMeshFingerprint.cs`
- Modify: `CScore/Planar/PlanarMeshSnapshot.cs`
- Modify: `CScore/Planar/PlanarMeshSnapshotValidator.cs`
- Modify: `CScore.Tests/Planar/PlanarGeometryFingerprintTests.cs`
- Modify: `CScore.Tests/Planar/PlanarMeshFingerprintTests.cs`
- Create: `CScore.Tests/Planar/PlanarConstraintFingerprintTests.cs`
- Create: `CScore.Tests/Planar/PlanarConstraintMeshMappingTests.cs`

- [ ] **Step 1: Write failing fingerprint and mapping tests.** Assert that changing constraint ID, point, facet kind, master reference, DOF mask or tolerance changes the region/mesh fingerprint; assert point mapping cardinality, unknown node, duplicate edge, discontinuous chain and incomplete region mappings produce diagnostics.

- [ ] **Step 2: Implement canonical constraint fingerprinting.** Extend `PlanarGeometryFingerprint.Compute()` with the constraint list, sort constraints by logical ID, preserve point order for directed curves/polygons, serialize all numeric values with invariant `G17`, and include every structural/mesh field named by the spec. Update `PlanarRegion.RecalcFingerprint()` to pass `ConstraintObjects`.

- [ ] **Step 3: Implement snapshot provenance records.** Add records for entity dimension/tag/physical group/name and for ordered curve edges. Extend `PlanarMeshSnapshot` with `MeshFormatVersion`, `EntityProvenance` and `ConstraintMappings`, preserving existing dense node/element constructors used by shell adapters.

- [ ] **Step 4: Extend snapshot validation.** Validate unique constraint IDs, known dense node/element indices, mapping cardinality, unique ordered edges, region element references and finite provenance values. A mapping diagnostic must be returned as an error when its requested mesh facet cannot be represented.

- [ ] **Step 5: Update mesh fingerprint contract.** Change the source marker to `planar-mesh-v2`, include `MSH 4.1`, and continue including settings, actual Gmsh version, generator version and the updated region fingerprint.

- [ ] **Step 6: Run focused tests and commit.**

Run: `dotnet test CScore.Tests/CScore.Tests.csproj --filter FullyQualifiedName~Planar --no-restore`

Expected: all existing planar tests plus new fingerprint/mapping tests pass.

```bash
git add CScore/Planar CScore.Tests/Planar
git commit -m "feat(planar): add constraint mesh mappings and provenance"
```

## Task 3: Implement a Strict MSH 4.1 Reader

**Files:**

- Create: `OpenCS.Gmsh/Parsing/GmshMsh41Document.cs`
- Create: `OpenCS.Gmsh/Parsing/GmshMsh41Reader.cs`
- Create: `OpenCS.Gmsh.Tests/GmshMsh41ReaderTests.cs`

- [ ] **Step 1: Add parser fixtures that initially fail.** Include a valid ASCII 4.1 document with point, line, triangle and quadrangle blocks; multiple node/element blocks; `$PhysicalNames`; `$Entities` physical tags; and malformed cases for wrong version, binary flag, missing sections, duplicate nodes, unknown node references, unsupported element types and truncated blocks.

```csharp
[Fact]
public void Read_MapsElementBlockEntityToPhysicalGroup()
{
    var document = GmshMsh41Reader.Read(ValidMixedFixture);

    var triangle = Assert.Single(document.Elements.Where(e => e.ElementType == 2));
    Assert.Equal(2001, triangle.PhysicalGroup);
    Assert.Equal(2, triangle.EntityDimension);
}
```

- [ ] **Step 2: Run the focused parser tests to verify failure.**

Run: `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --filter FullyQualifiedName~GmshMsh41ReaderTests --no-restore`

Expected: compilation failure because the reader and document types do not exist.

- [ ] **Step 3: Implement section parsing.** Parse `$MeshFormat` strictly as version 4.1, ASCII file type 0; parse quoted physical names; parse point/curve/surface entity records from `$Entities` and associate their physical tags; parse node blocks into raw IDs and coordinates; parse element blocks into raw element IDs, entity dimension/tag, type and node IDs.

- [ ] **Step 4: Implement linear element normalization.** Support types 15/1/2/3, retain unsupported types as blocking diagnostics, resolve every raw node reference, reject duplicate raw IDs and truncated blocks, and expose parsed entity/physical provenance without assigning OpenCS IDs.

- [ ] **Step 5: Run parser tests and commit.**

Run: `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --filter FullyQualifiedName~GmshMsh41ReaderTests --no-restore`

Expected: all valid fixtures pass and malformed fixtures return the expected blocking diagnostics or typed parse exception.

```bash
git add OpenCS.Gmsh/Parsing OpenCS.Gmsh.Tests/GmshMsh41ReaderTests.cs
git commit -m "feat(gmsh): add strict msh 4.1 reader"
```

## Task 4: Generate Deterministic Constraint Geometry

**Files:**

- Create: `OpenCS.Gmsh/Generation/GmshPlanarGeoBuilder.cs`
- Modify: `OpenCS.Gmsh/GmshPlanarMesher.cs`
- Modify: `OpenCS.Gmsh.Tests/GmshPlanarMesherTests.cs`
- Create: `OpenCS.Gmsh.Tests/GmshPlanarGeoBuilderTests.cs`

- [ ] **Step 1: Add generator tests for deterministic output.** Assert identical region/settings/constraints produce byte-identical `.geo`; assert generated physical names include each logical constraint ID; assert point, curve and region geometry is inside the host Plane Surface and a region is not emitted as a hole.

- [ ] **Step 2: Run generator tests to verify failure.**

Run: `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --filter FullyQualifiedName~GmshPlanarGeoBuilderTests --no-restore`

Expected: compilation failure because the extracted builder does not exist.

- [ ] **Step 3: Extract host generation without changing semantics.** Move current contour/line numbering and boundary physical-group logic out of `GmshPlanarMesher` into the builder, retaining deterministic hull/hole numbering and mesh settings.

- [ ] **Step 4: Add deterministic constraint allocation.** Sort constraint IDs, allocate non-colliding physical groups by dimension/kind, emit explicit `$PhysicalNames`-compatible names, and keep all generated IDs separate from domain IDs. Use the current local `U/V` coordinates and characteristic length.

- [ ] **Step 5: Emit mesh loci.** Emit Gmsh point entities for `EmbeddedPoint`, line/polyline entities embedded into the host surface for `EmbeddedCurve`, and internal polygon geometry for `EmbeddedRegion`/`ConformingPartition`. Use internal Boolean fragmentation or equivalent Gmsh topology so a conforming region partitions host material without becoming a hole; do not emit artificial supports or solver constraints.

- [ ] **Step 6: Switch the process command to MSH 4.1.** Change the output command from `-format msh22` to `-format msh41`, update the generator version, and keep `.geo`, `.msh`, logs and manifest archive behavior unchanged.

- [ ] **Step 7: Run generator/unit tests and commit.**

Run: `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --filter FullyQualifiedName~GmshPlanarGeoBuilderTests --no-restore`

Expected: deterministic generator tests pass.

```bash
git add OpenCS.Gmsh OpenCS.Gmsh.Tests
git commit -m "feat(gmsh): generate constraint loci in planar geo"
```

## Task 5: Map MSH 4.1 Entities to Snapshot Constraints

**Files:**

- Create: `OpenCS.Gmsh/Mapping/PlanarConstraintMeshMapper.cs`
- Modify: `OpenCS.Gmsh/GmshPlanarMesher.cs`
- Modify: `CScore/Planar/PlanarMeshSnapshotValidator.cs` if cross-project validation needs a domain-side entry point.
- Modify: `OpenCS.Gmsh.Tests/GmshPlanarMesherTests.cs`
- Create: `OpenCS.Gmsh.Tests/PlanarConstraintMeshingTests.cs`

- [ ] **Step 1: Add mapping tests using parsed fixtures.** Test exact point node matching, out-of-tolerance rejection, a two-edge ordered curve chain, gaps/branches/wrong endpoints, region element/node sets, unknown physical groups and duplicate coincident nodes.

- [ ] **Step 2: Run mapping tests to verify failure.**

Run: `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --filter FullyQualifiedName~PlanarConstraint --no-restore`

Expected: failures or compilation errors until the mapper is implemented.

- [ ] **Step 3: Implement dense snapshot conversion.** Convert parsed raw nodes to `PlanarMeshNode` using `Frame3D.Origin + LocalX * U + LocalY * V`; normalize T3/Q4 orientation to positive local area; retain existing host boundary mappings.

- [ ] **Step 4: Implement point mapping.** Select only nodes whose squared local distance is within the object tolerance; reject zero candidates and more than one candidate with distinct diagnostics; never accept an unqualified nearest node.

- [ ] **Step 5: Implement curve mapping.** Gather line elements/entities belonging to the constraint physical group, normalize each segment to dense node indices, build adjacency, walk from the declared first endpoint to the last, and require a single continuous path with no branch, duplicate edge, gap or uncovered length.

- [ ] **Step 6: Implement region mapping.** Gather surface elements by the constraint physical group/entity provenance, include their unique nodes, verify each element lies in the polygon/host region, reject hole crossings and preserve host surface material. For `ConformingPartition`, require the locus boundary to be represented by shell edges.

- [ ] **Step 7: Build the final calculability result.** Add entity provenance and mappings to the snapshot, run `PlanarMeshSnapshotValidator`, append all diagnostics, and set `IsCalculable` only when host and requested constraint mappings are complete.

- [ ] **Step 8: Run focused mapping tests and commit.**

Run: `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --filter FullyQualifiedName~PlanarConstraint --no-restore`

Expected: point, curve, region and failure-path mapping tests pass.

```bash
git add OpenCS.Gmsh OpenCS.Gmsh.Tests CScore/Planar
git commit -m "feat(gmsh): map msh 4.1 constraint entities"
```

## Task 6: Persist Constraints, Snapshot Metadata and Mappings

**Files:**

- Modify: `OpenCS/Utilites/DatabaseService.cs`
- Modify: `OpenCS.Gmsh.Tests/PlanarMeshPersistenceTests.cs`

- [ ] **Step 1: Add failing SQLite round-trip tests.** Save a `PlanarRegion` with point/curve/region constraints and verify all geometry/facets/master references survive reload. Save a snapshot with format `4.1`, entity provenance and all mapping collections and verify they survive reload without touching `fem_mesh_*`.

- [ ] **Step 2: Run persistence tests to verify failure.**

Run: `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --filter FullyQualifiedName~PlanarMeshPersistenceTests --no-restore`

Expected: failures because v46 schema and CRUD do not contain constraint columns/tables.

- [ ] **Step 3: Add schema v47.** Set `CurrentSchemaVersion = 47`; add `constraint_objects_json` to the final `planar_regions` definition; add `mesh_format_version` and `entity_provenance_json` to `planar_mesh_snapshots`; add `planar_mesh_constraint_mappings`; add `if (i == 46) { MigrateV47(); continue; }` and make `MigrateV47()` use `ColumnExists` plus idempotent `CREATE TABLE IF NOT EXISTS`.

- [ ] **Step 4: Update region CRUD.** Add `constraint_objects_json` to `AddPlanarRegion`, `UpdatePlanarRegion`, `GetPlanarRegions` SQL and parameter/index handling. Serialize the explicit-discriminator domain model with existing `_jsonSettings`; deserialize missing/NULL legacy values as `[]`; call `region.RecalcFingerprint()` in `AddPlanarRegionParameters()` before writing the fingerprint so current constraints cannot be omitted from a newly saved region, while `GetPlanarRegions()` retains the stored fingerprint until the caller changes the model.

- [ ] **Step 5: Update snapshot CRUD atomically.** Insert/read format and provenance metadata in the snapshot header, insert each `PlanarConstraintMeshMapping` inside the existing transaction, and load mappings ordered by logical constraint ID. Define `mesh_format_version TEXT NOT NULL DEFAULT 'msh22'` and `entity_provenance_json TEXT NOT NULL DEFAULT '[]'` for legacy compatibility; new Gmsh 4.1 snapshots write `msh41`. Keep all SQL parameterized and preserve cascade deletion through `snapshot_id` foreign keys.

- [ ] **Step 6: Run persistence and migration tests and commit.**

Run: `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --filter FullyQualifiedName~PlanarMeshPersistenceTests --no-restore`

Expected: new and existing round-trip tests pass, including a fresh v47 database and a database migrated from v46.

```bash
git add OpenCS/Utilites/DatabaseService.cs OpenCS.Gmsh.Tests/PlanarMeshPersistenceTests.cs
git commit -m "feat(db): persist planar constraint mappings"
```

## Task 7: Run Real Gmsh Constraint Scenarios

**Files:**

- Modify: `OpenCS.Gmsh.Tests/GmshPlanarMesherTests.cs`
- Modify: `OpenCS.Gmsh.Tests/PlanarConstraintMeshingTests.cs`

- [ ] **Step 1: Add real executable tests.** Use the fixed executable path already used by current tests and unique artifact roots. Cover a host rectangle with one hole, an exact embedded point, an internal polyline, an internal rigid-zone polygon with `ConformingPartition`, and a mesh-only partition.

- [ ] **Step 2: Assert the complete contract.** For every successful case assert `IsCalculable`, MSH 4.1 provenance, nonempty elements, exact point mapping, continuous curve mapping, region element mapping, preserved hole, no duplicate coincident nodes and physical/entity provenance tied to the logical constraint ID.

- [ ] **Step 3: Add blocking scenarios.** Feed an out-of-host point, a curve crossing a hole, an overlapping region and a malformed/incomplete mapping fixture; assert a blocking diagnostic, `IsCalculable == false`, and no silent fallback to nearest node.

- [ ] **Step 4: Run the integration tests and commit.**

Run: `dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --filter FullyQualifiedName~PlanarConstraintMeshingTests --no-restore`

Expected: all real-Gmsh success and blocking scenarios pass; artifacts remain available for successful and failed operations.

```bash
git add OpenCS.Gmsh.Tests
git commit -m "test(gmsh): cover real constraint meshing"
```

## Task 8: Full Verification and Final Review

- [ ] **Step 1: Run the domain and Gmsh suites.**

```bash
dotnet test CScore.Tests/CScore.Tests.csproj --no-restore
dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore
dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --no-restore
```

Expected: all relevant tests pass; the known xUnit skip in `CScore.Tests` remains the only expected skipped test. If the known SQLite cleanup race recurs in `OpenCS.OpenSees.Tests`, rerun once sequentially and record it separately from Gmsh changes.

- [ ] **Step 2: Build the complete solution.**

Run: `dotnet build OpenCS.sln --no-restore`

Expected: zero errors; only the two existing `OpenCS.Core.UI` missing-project warnings may remain.

- [ ] **Step 3: Review the diff.** Confirm no changes to `fem_mesh_*`, no solver commands, no WPF strings/UI, no unparameterized SQL, no nearest-node fallback, and no generated artifacts committed.

```bash
git status -sb
```

- [ ] **Step 4: Commit any final test-only/documentation corrections.** Use a focused message and keep implementation commits intact; do not amend earlier commits.

## Execution Order

Run tasks in order. Each task is independently testable and committed before the next one starts. The implementation remains on `feature/gmsh-planar-constraints-slice5`; no merge, push, or pull request is part of this plan.
