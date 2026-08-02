# Gmsh Solver-Level OpenSees Constraints Slice 5 Implementation Plan

> **Для inline execution:** план выполняется основным агентом в текущей ветке. Субагенты и `subagent-driven-development` не используются по прямому указанию пользователя и правилам `AGENTS.md`. Шаги отмечаются через checkbox.

**Goal:** Явно переводить проверенные `PlanarConstraintObject` в `equalDOF` и `rigidLink` нормализованной OpenSees-модели с атомарной ошибкой, строгой correspondence и provenance.

**Architecture:** Добавить отдельный bridge-адаптер в `OpenCS.OpenSees.CScore`; mesh-адаптер остаётся только геометрическим. Адаптер получает `PlanarMeshShellModelResult`, `PlanarMeshSnapshot`, effective constraints, `FemSchemaTopology` и explicit source-node-to-OpenSees-tag map, строит полный candidate set и возвращает новую модель либо `null` при blocking diagnostics.

**Tech Stack:** C#/.NET 9, `CScore.Planar`, `CScore.Fem`, `OpenCS.OpenSees.Structural`, xUnit, настоящий `C:\Tools\OpenSees\bin\OpenSees.exe`.

**Specification:** `docs/superpowers/specs/2026-08-02-gmsh-solver-constraints-slice5-design.md`

---

## Research Baseline

- `OpenCS.OpenSees.Structural.ShellOpenSeesModel` уже содержит `EqualDofConstraints` и `RigidLinks`, проверяет неизвестные узлы, пустые/невалидные DOF и конфликт slave DOF, но `Validate()` требует хотя бы одну stage.
- `OpenCS.OpenSees.Tcl.ShellTclGenerator` вызывает `model.Validate()` перед генерацией и уже эмитит `equalDOF` и `rigidLink`; его менять не нужно.
- `OpenCS.OpenSees.CScore.PlanarMeshSnapshotShellModelAdapter` создаёт shell nodes с tag `snapshot index + 1` и возвращает `NodeIndexToTag`; stages намеренно остаются пустыми.
- `PlanarMeshSnapshot.ConstraintMappings` хранит exact point node, ordered curve edges, source references и structural relations; dense snapshot nodes содержат мировые `X/Y/Z`.
- `PlanarStructuralRelation` хранит source member/element IDs, master reference, kind и DOF; source node IDs берутся из соответствующего `PlanarSourceReference`.
- `FemSchemaTopology` содержит read-only arrays `FemNode`, `FemMember`, `FemElement`; connectivity элементов хранится JSON-массивом `FemElement.NodeIdsJson`.
- `OpenCS.OpenSees.Tests` использует xUnit и реальные OpenSees integration tests; текущий полный набор имеет известную SQLite cleanup race в трёх persistence-тестах при параллельном запуске.

## File Map

### Create

- `OpenCS.OpenSees.CScore/PlanarOpenSeesConstraintOptions.cs` — policy enum и настройки `EmbeddedMember`/`RigidBody`.
- `OpenCS.OpenSees.CScore/PlanarOpenSeesConstraintResult.cs` — result/emission/provenance records.
- `OpenCS.OpenSees.CScore/PlanarStructuralOpenSeesAdapter.cs` — preflight, exact correspondence, relation translation и atomic result.
- `OpenCS.OpenSees.Tests/PlanarStructuralOpenSeesAdapterTests.cs` — pure unit tests for point/curve/policy/error paths.
- `OpenCS.OpenSees.Tests/PlanarStructuralOpenSeesIntegrationTests.cs` — real OpenSees tests proving equalDOF and eccentric rigidLink.

### Modify

- `docs/superpowers/specs/2026-08-02-gmsh-solver-constraints-slice5-design.md` — only if implementation review finds a contract correction; current plan already includes the approved offset and staged-validation corrections.
- `OpenCS.OpenSees.Tests/ShellBeamConnectionFixtures.cs` — only if a shared fixture helper is required; prefer existing `with { EqualDofConstraints = [], RigidLinks = [] }` paths.

No `CScore`, SQLite, WPF, `OpenCS.Gmsh` or `OpenCS.OpenSees` production changes are expected.

## Task 1: Add Result and Policy Contracts

**Files:**

- Create: `OpenCS.OpenSees.CScore/PlanarOpenSeesConstraintOptions.cs`
- Create: `OpenCS.OpenSees.CScore/PlanarOpenSeesConstraintResult.cs`
- Test: `OpenCS.OpenSees.Tests/PlanarStructuralOpenSeesAdapterTests.cs`

- [x] **Step 1: Write the first failing contract test.** Add a test that constructs a `PlanarOpenSeesConstraintOptions`, verifies defaults (`EmbeddedMemberPolicy == EqualDof`, `RigidBodyPolicy == RigidLinkBeam`), and checks that a result exposes `IsCalculable`, diagnostics and emissions.

```csharp
[Fact]
public void ConstraintOptions_DefaultToStrictOpenSeesPolicies()
{
    var options = new PlanarOpenSeesConstraintOptions();

    Assert.Equal(PlanarOpenSeesConstraintPolicy.EqualDof, options.EmbeddedMemberPolicy);
    Assert.Equal(PlanarOpenSeesConstraintPolicy.RigidLinkBeam, options.RigidBodyPolicy);
}
```

- [x] **Step 2: Run the focused test and verify it fails.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~PlanarStructuralOpenSeesAdapterTests --no-restore`

Expected: compilation failure because the policy/result types and test file do not exist.

- [x] **Step 3: Implement the contracts.** Add Russian XML documentation and use immutable records where possible:

```csharp
public enum PlanarOpenSeesConstraintPolicy
{
    EqualDof,
    RigidLinkBar,
    RigidLinkBeam
}

public sealed record PlanarOpenSeesConstraintOptions
{
    public PlanarOpenSeesConstraintPolicy EmbeddedMemberPolicy { get; init; } =
        PlanarOpenSeesConstraintPolicy.EqualDof;
    public PlanarOpenSeesConstraintPolicy RigidBodyPolicy { get; init; } =
        PlanarOpenSeesConstraintPolicy.RigidLinkBeam;
}

public sealed record PlanarOpenSeesConstraintEmission(
    string ConstraintObjectId,
    PlanarStructuralKind StructuralKind,
    PlanarOpenSeesConstraintPolicy Policy,
    int SourceMemberId,
    string SourceMemberTag,
    IReadOnlyList<int> SourceElementIds,
    IReadOnlyList<string> SourceElementTags,
    int MasterNodeTag,
    int SlaveNodeTag,
    IReadOnlyList<int> Dofs,
    IReadOnlyList<int> HostSnapshotNodeIndices,
    IReadOnlyList<int> SourceNodeIds);

public sealed record PlanarOpenSeesConstraintResult(
    ShellOpenSeesModel? Model,
    IReadOnlyList<PlanarOpenSeesConstraintEmission> Emissions,
    IReadOnlyList<FemValidationDiagnostic> Diagnostics)
{
    public bool IsCalculable => Model is not null &&
        !Diagnostics.Any(diagnostic => diagnostic.IsError);
}
```

- [x] **Step 4: Run the focused contract test.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~PlanarStructuralOpenSeesAdapterTests.ConstraintOptions_DefaultToStrictOpenSeesPolicies --no-restore`

Expected: PASS.

## Task 2: Implement Strict Point Relations

**Files:**

- Create: `OpenCS.OpenSees.CScore/PlanarStructuralOpenSeesAdapter.cs`
- Modify: `OpenCS.OpenSees.Tests/PlanarStructuralOpenSeesAdapterTests.cs`

- [x] **Step 1: Add failing point tests and minimal fixtures.** Build a four-node shell model plus explicit source node tags, a one-point `PlanarMeshSnapshot`, one `PlanarConstraintMeshMapping`, a `FemSchemaTopology` with source node/member and a source-node-to-OpenSees map. Cover:

  - coincident `EmbeddedMember` + `EqualDof` emits master source tag, host shell tag and sorted six DOF;
  - offset point + `RigidLinkBeam` emits a rigid link without coordinate matching;
  - offset point + `EqualDof` returns `Model == null` and `planar_opensees_equal_dof_coordinates_mismatch`;
  - `PointMpc` returns `Model == null` and `planar_opensees_unsupported_mpc`;
  - source master absent from the model returns `Model == null` and does not emit a partial relation.

- [x] **Step 2: Run the point tests to verify failure.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~PlanarStructuralOpenSeesAdapterTests --no-restore`

Expected: compilation failure because `PlanarStructuralOpenSeesAdapter.Apply` does not exist.

- [x] **Step 3: Implement the adapter entry point and point preflight.** Use this public shape:

```csharp
public static PlanarOpenSeesConstraintResult Apply(
    PlanarMeshShellModelResult shellResult,
    PlanarMeshSnapshot snapshot,
    IReadOnlyList<PlanarConstraintObject> constraints,
    FemSchemaTopology topology,
    IReadOnlyDictionary<int, int> sourceNodeTagById,
    PlanarOpenSeesConstraintOptions? options = null)
```

Validate null inputs, duplicate constraint IDs, snapshot mapping IDs, `snapshot.IsCalculable`, finite positive `ToleranceM`, host index cardinality, source references, source node map and source OpenSees node existence. Process constraints sorted by `Id`; `StructuralKind.None` is mesh-only and produces no solver relation.

- [x] **Step 4: Implement policy and DOF conversion.** Resolve effective relation data from the mapping first, falling back to the object when the mapping carries no structural provenance. Match a relation to `PlanarSourceReference` by `(SourceMemberId, SourceMemberTag)`. For point relations require exactly one source node ID and one host point index. Resolve the mask in this order: non-empty relation `DofMask`, then constraint `DofMask`, then `StructuralFacet.DofMask`; convert it to ascending DOF numbers `[1..6]` and reject `None`.

  - `Tie` always uses `EqualDof`;
  - `EmbeddedMember` uses `options.EmbeddedMemberPolicy`;
  - `RigidBody` uses `options.RigidBodyPolicy`;
  - `PointMpc`, `Support` and `Symmetry` are blocking diagnostics.

`EqualDof` requires source and host coordinates within `ToleranceM`; `RigidLinkBar` requires exactly translations; `RigidLinkBeam` requires all six DOF. Never select a source or host node by minimum distance.

- [x] **Step 5: Add atomic emission and conflict checks.** Accumulate candidates locally. Check master/slave inequality, duplicate exact relations and slave DOF ownership before constructing `model with { EqualDofConstraints = ..., RigidLinks = ... }`. On any error return `Model = null` and empty emitted constraints; on success preserve existing model relations and append deterministic new ones. Do not call full `ShellOpenSeesModel.Validate()` because the mesh adapter may return a model without stages.

- [x] **Step 6: Run point tests.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~PlanarStructuralOpenSeesAdapterTests --no-restore`

Expected: all point/policy tests PASS.

## Task 3: Add Exact Curve Correspondence

**Files:**

- Modify: `OpenCS.OpenSees.CScore/PlanarStructuralOpenSeesAdapter.cs`
- Modify: `OpenCS.OpenSees.Tests/PlanarStructuralOpenSeesAdapterTests.cs`

- [x] **Step 1: Add failing curve tests.** Build a two-edge host chain and a two-element source beam chain. Cover:

  - forward `OrderedCurveEdges` with coincident nodes emits one relation per paired node;
  - reversed host chain is accepted without changing source/host semantics;
  - different chain lengths, branch/gap, missing source element node and source cycle return blocking diagnostics;
  - an error on one curve leaves `Emissions` empty and `Model == null`, even when a preceding point is valid;
  - `RigidLinkBeam` accepts an explicitly paired offset chain, while `EqualDof` rejects the same offset.

- [x] **Step 2: Run curve tests to verify failure.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~PlanarStructuralOpenSeesAdapterTests --no-restore`

Expected: curve tests fail because only point mapping is implemented.

- [x] **Step 3: Reconstruct the host node chain.** Convert `OrderedCurveEdges` into one ordered dense-index sequence. Accept either orientation of each edge only when it continues the previous endpoint; reject duplicate edges, gaps, branches, empty chains and non-unique node visits. Resolve every dense index against `snapshot.Nodes` and `shellResult.NodeIndexToTag`.

- [x] **Step 4: Reconstruct the source chain.** Resolve relation source references and source element IDs through `FemSchemaTopology`. Parse every `NodeIdsJson` with `JsonSerializer`; use 2-node beam connectivity to build an endpoint-to-endpoint chain, or the source member connectivity when no source elements exist. Reject missing nodes, branching, cycles and a source chain whose node count differs from the host chain.

- [x] **Step 5: Pair and emit the whole chain.** For `EqualDof`, compare source `FemNode` coordinates with host `PlanarMeshNode` coordinates in direct and reverse order using the object tolerance. For rigid links, use only the topology-derived order and permit offset. Emit all pairs or none; each pair carries the same structural relation, policy, DOF and source provenance.

- [x] **Step 6: Run focused tests.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~PlanarStructuralOpenSeesAdapterTests --no-restore`

Expected: point and curve tests PASS with no partial relation output.

## Task 4: Harden Provenance and Structural Preflight

**Files:**

- Modify: `OpenCS.OpenSees.CScore/PlanarStructuralOpenSeesAdapter.cs`
- Modify: `OpenCS.OpenSees.Tests/PlanarStructuralOpenSeesAdapterTests.cs`

- [x] **Step 1: Add failing validation tests.** Cover duplicate constraint ID, mapping missing for an effective structural object, mapping with duplicate/unknown host index, empty DOF mask, source reference mismatch, source/host same tag, conflicting equalDOF and rigidLink ownership of one slave DOF, deterministic relation ordering and complete emission provenance.

- [x] **Step 2: Implement stable diagnostics.** Use stable codes such as `planar_opensees_constraint_mapping_missing`, `planar_opensees_host_node_unknown`, `planar_opensees_source_node_unknown`, `planar_opensees_dof_invalid`, `planar_opensees_constraint_conflict`, and `planar_opensees_unsupported_mpc`. Include constraint ID, source member and node/tag in every relevant message.

- [x] **Step 3: Implement deterministic output.** Sort source relations by source member ID/tag, host snapshot index and source node ID. Deduplicate only byte-for-byte identical emitted relations; treat different policies, masters or DOF ownership as a blocking conflict. Preserve pre-existing model constraints and append generated relations in the same stable order.

- [x] **Step 4: Verify staged model behavior.** Add a test that applies the adapter to a model with existing `Stages`, then calls `new ShellTclGenerator().Generate(result.Model!)`; assert validation succeeds and the generated Tcl contains `equalDOF`/`rigidLink` exactly once per emission. Keep the adapter itself independent of stages.

- [x] **Step 5: Run all adapter tests.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~PlanarStructuralOpenSeesAdapterTests --no-restore`

Expected: all unit, atomicity, provenance and Tcl-order tests PASS.

## Task 5: Prove Real OpenSees Behavior

**Files:**

- Create: `OpenCS.OpenSees.Tests/PlanarStructuralOpenSeesIntegrationTests.cs`
- Modify: only if necessary `OpenCS.OpenSees.Tests/Fixtures/ShellBeamConnectionFixtures.cs`

- [x] **Step 1: Add real equalDOF integration test.** Start from `ShellBeamConnectionFixtures.EqualDofSeam()` with `EqualDofConstraints = []`. Build a snapshot whose host indices map to tags 6 and 7, source FEM nodes 2 and 3 map to tags 2 and 3, and effective point constraints carry exact coincident coordinates. Apply the adapter, run the existing `ShellTclGenerator`/`OpenSeesProcessRunner` path, and assert coincident displacements and global `Fz` equilibrium.

- [x] **Step 2: Add real rigidLink integration test.** Start from `ShellBeamConnectionFixtures.RigidLinkOffset()` with `RigidLinks = []`. Map source FEM node 2 to OpenSees tag 2 and host point index 4 to offset tag 5. Use `RigidLinkBeam` with all six DOF, apply the adapter, run actual OpenSees, and assert total `Fx` equilibrium and support moment close to `Fx * 0.5`.

- [x] **Step 3: Keep test artifact handling consistent.** Use `ShellArtifactFixture`, `OpenSeesTestExecutable.ResolveOrSkip()`, a unique script path and `ShellResultParser`, exactly as `ShellBeamConnectionIntegrationTests` does. Do not search PATH for OpenSees.

- [x] **Step 4: Run the integration tests.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~PlanarStructuralOpenSeesIntegrationTests --no-restore`

Expected: real OpenSees tests PASS when executable is available, or are skipped only by the existing executable resolver.

## Task 6: Full Verification and Review

- [x] **Step 1: Run focused suites.**

```powershell
dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --filter FullyQualifiedName~PlanarStructuralOpenSees --no-restore
dotnet test CScore.Tests/CScore.Tests.csproj --no-build --no-restore
```

Expected: all new adapter/integration tests pass; `CScore.Tests` remains at its baseline `394 passed, 1 skipped` or higher.

- [x] **Step 2: Build the complete solution.**

Run: `dotnet build OpenCS.sln --no-restore`

Expected: zero errors; only the two existing missing `OpenCS.Core.UI` warnings may remain.

- [x] **Step 3: Run the full OpenSees suite once.**

Run: `dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --no-build --no-restore`

Expected: new tests pass. Record the known SQLite cleanup race separately if it recurs; do not change production behavior or test tolerances to hide it.

- [x] **Step 4: Review the diff and worktree.** Confirm that no UI, SQLite, CSfea, `OpenCS.Gmsh`, `fem_mesh_*`, nearest-node fallback or generated artifacts were changed. Confirm the only planned production files are the three bridge files and tests/docs.

Run: `git diff --check` and `git status -sb`.

- [x] **Step 5: Update the roadmap memory only if requested.** Do not modify the user’s Obsidian notes automatically; report the implementation result and test evidence in the final response.

## Execution Order

Run tasks 1 through 6 in order. After each TDD red/green step, update this plan’s checkboxes. No merge, push, pull request or commit is part of this execution unless the user explicitly requests Git history operations.

## Inline Plan Review

План проверен после написания против утверждённой спецификации и текущих
контрактов репозитория:

- сохранён отдельный bridge-адаптер; mesh, Gmsh, SQLite, WPF и CSfea не
  расширяются;
- учтено, что mesh adapter возвращает модель без `Stages`, поэтому полная
  `ShellOpenSeesModel.Validate()` остаётся на границе Tcl generator;
- `equalDOF` требует совпадающих координат, а explicit `rigidLink` допускает
  offset, что позволяет проверить эксцентричную связь без nearest-node поиска;
- atomic result не выпускает частичные relations при любой blocking diagnostic;
- provenance содержит source member/element/node и host snapshot/OpenSees tags;
- baseline race в SQLite-тестах зафиксирована как pre-existing и не влияет на
  критерии готовности нового bridge.

Незаполненных `TODO`/`TBD`, неоднозначных файловых путей и лишних подсистем в
плане не осталось. План готов к inline execution.
