# Parent Boundary Contract Implementation Plan

> Для этого репозитория план выполняется inline согласно `AGENTS.md` и прямому запросу пользователя; subagent-driven workflow не используется.

**Goal:** реализовать mesh-независимый контракт parent/template boundary actions для cut interfaces вертикального planar fragment и довести его до mapping на Gmsh snapshot, OpenSees и CSfea.

**Architecture:** доменные типы в `CScore.Planar` описывают cut interface, six-DOF modes, force/kinematic samples, source provenance и строгий `parent/template/combined` resolver. Mesh mapper использует существующие ordered constraint/boundary mappings и выдаёт solver-независимые nodal actions. OpenSees и CSfea получают отдельные adapters, причём prescribed displacement остаётся явным и не превращается в `fix`.

**Tech Stack:** .NET 9, C#, `CScore`, `OpenCS.Gmsh`, `OpenCS.OpenSees`, `OpenCS.OpenSees.CScore`, `CSfea.Core`, xUnit, hand-rolled `CSfea.Tests`, реальный `gmsh.exe` и `OpenSees.exe` в integration tests.

---

## Scope and Execution Rules

- Работать в ветке `feature/gmsh-parent-boundary-contract`.
- Не добавлять WPF, SQLite persistence, автоматическое извлечение shell-группы, multi-region assembly, springs/contact или восстановление нелинейной parent history.
- Использовать существующий `PlanarDofMask`, `Frame3D`, `PlanarVector3`, `PlanarConstraintObject`, `PlanarConstraintMeshMapping` и `PlanarMeshSnapshot`.
- Не использовать Gmsh tags как domain IDs или OpenSees tags.
- Не смешивать parent/template actions на одном DOF и не складывать их автоматически.
- Все новые public domain types получить русские XML-doc комментарии по соглашению проекта.
- Не создавать commits в рамках выполнения без отдельного запроса пользователя; после задач оставлять изменения в рабочей ветке и проверять `git diff`.

## Existing Code to Reuse

- `CScore/Planar/PlanarVector3.cs`: finite 3D vectors and `Dot`/`Cross`.
- `CScore/Planar/Frame3D.cs`: validated right-handed frame; conversion helper will use `Origin`, `LocalX`, `LocalY`, `LocalZ`.
- `CScore/Planar/PlanarConstraintObject.cs`: `PlanarDofMask`, curve geometry and conforming mesh facet.
- `CScore/Planar/PlanarConstraintMeshMapping.cs`: ordered curve edges and dense snapshot indices.
- `CScore/Planar/PlanarMeshSnapshot.cs`: `PlanarMeshNode`, `PlanarMeshEdge`, boundary mappings and snapshot fingerprint.
- `OpenCS.Gmsh/Mapping/PlanarConstraintMeshMapper.cs`: actual MSH 4.1 curve mapping and blocking diagnostics.
- `CScore/Planar/PlanarLoadMapper.cs`: balance-check pattern and consistent edge integration style.
- `OpenCS.OpenSees.CScore/PlanarMeshSnapshotShellModelAdapter.cs`: snapshot-index to OpenSees-tag provenance.
- `OpenCS.OpenSees/Structural/ShellOpenSeesModel.cs`: normalized shell model and fixed DOF validation.
- `OpenCS.OpenSees/Tcl/ShellTclGenerator.cs`: deterministic shell Tcl and staged load patterns.
- `CSfea.Core/Bc/BoundaryConditions.cs`: explicit fixed DOF versus `uFixed` support.

## Task 1: Add Core Cut Interface and Action Contracts

**Files:**

- Create: `CScore/Planar/PlanarCutInterface.cs`.
- Create: `CScore/Planar/PlanarBoundaryAction.cs`.
- Create: `CScore/Planar/PlanarBoundaryActionValidation.cs`.
- Test: `CScore.Tests/Planar/PlanarBoundaryActionTests.cs`.

### Step 1: Write failing domain validation tests

Add xUnit tests covering:

```csharp
[Fact]
public void CutInterface_ValidatesCurveNormalAndUniqueId()
{
    var cut = new PlanarCutInterface
    {
        Id = "top",
        Geometry = new PlanarConstraintGeometry(
            PlanarConstraintGeometryKind.Curve,
            [new(0, 1), new(2, 1)]),
        NormalFromFragmentToOmittedSide = new(0, 1, 0),
        ModeByDof = PlanarBoundaryModeByDof.All(PlanarBoundaryDofMode.Free)
    };

    Assert.Empty(cut.Validate());
}

[Fact]
public void ModeByDofReturnsExplicitModeForEachDof()
{
    var modes = PlanarBoundaryModeByDof.None
        .With(PlanarDofMask.UX, PlanarBoundaryDofMode.Force);

    Assert.Equal(PlanarBoundaryDofMode.Force, modes.Get(PlanarDofMask.UX));
}

[Fact]
public void ForceSampleRequiresOrderedNormalizedS()
{
    var action = new PlanarBoundaryForceAction
    {
        InterfaceId = "top",
        Samples = [
            new(0.75, new(1, 0, 0), PlanarVector3.Zero),
            new(0.25, new(1, 0, 0), PlanarVector3.Zero)
        ]
    };

    var diagnostics = action.Validate();

    Assert.Contains(diagnostics, d => d.Code == "planar_boundary_samples_not_ordered");
}
```

Also cover finite vectors, `s` range `[0,1]`, non-empty force/kinematic coverage,
nonzero interface normal, duplicate DOF assignment and `preserve_support` without
source provenance.

Run:

```text
dotnet test CScore.Tests/CScore.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarBoundaryActionTests
```

Expected: FAIL because the new types do not exist.

### Step 2: Implement the smallest core model

Define in `PlanarBoundaryAction.cs`:

- `PlanarBoundaryActionSourceMode`: `Parent`, `Template`, `Combined`.
- `PlanarBoundaryActionKind`: `Force`, `Kinematic`.
- `PlanarBoundaryDofMode`: `Force`, `Kinematic`, `PreserveSupport`, `Free`, `Incomplete`.
- `PlanarBoundaryUnitSystem`: `Si` for the first implementation; reject other values instead of silently converting them.
- `PlanarBoundaryInterpolationKind`: `Uniform` and `Linear`; no implicit interpolation policy.
- `PlanarBoundaryModeByDof`: six named DOF values, `None`, `All(mode)`, `With(mask, mode)`, `Get(mask)` and `Validate()`; assignment of a different mode to an already assigned DOF is an error.
- `PlanarBoundaryForceSample`: `S`, `ForcePerLength`, `MomentPerLength`.
- `PlanarBoundaryKinematicSample`: `S`, `Displacement`, `Rotation`.
- `PlanarBoundarySourceReference`: source kind, source identity, optional member/element/node IDs, result identity and free-form field identity for templates.
- `PlanarBoundaryForceAction` and `PlanarBoundaryKinematicAction`: interface ID, covered DOF mask, normalized frame, units, interpolation kind, reference point, ordered samples and source references.
- `PlanarBoundaryActionSet`: source mode, interface actions, covered DOF, diagnostics, balance tolerances and provenance; `IsCalculable` is false when any diagnostic is an error.

Define in `PlanarCutInterface.cs`:

- `PlanarCutInterfaceKind`: `BottomCut`, `TopCut`, `SideCut`.
- `PlanarCutInterface`: ID, kind, curve `PlanarConstraintGeometry`, global normal from fragment to omitted side, `Frame3D`, modes by DOF, optional mesh constraint ID, optional `PlanarBoundaryKey`, tolerance and omitted-side reference.
- `CreateMeshConstraint()`: creates a request-local `PlanarConstraintObject.Curve` with `PlanarMeshKind.ConformingPartition` and a deterministic ID when the cut is internal or has no boundary key.

Define in `PlanarBoundaryActionValidation.cs`:

- finite and ordered sample checks;
- action kind versus mode coverage checks;
- unique interface IDs and source reference checks;
- no implicit zero coverage;
- diagnostics using `FemValidationDiagnostic` codes beginning with `planar_boundary_`.

Keep sample vectors in the action frame until the explicit normalizer in Task 2;
do not infer signs from `TopCut`/`BottomCut` names.

### Step 3: Run focused tests

Run the focused xUnit command from Step 1. Expected: all new domain tests pass.

Then run:

```text
dotnet test CScore.Tests/CScore.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarBoundaryActionTests
```

Expected: all focused tests pass and existing tests remain unaffected.

## Task 2: Normalize Parent and Template Sources

**Files:**

- Create: `CScore/Planar/IPlanarBoundaryActionProvider.cs`.
- Create: `CScore/Planar/PlanarBoundaryActionRequest.cs`.
- Create: `CScore/Planar/PlanarBoundaryTemplateProvider.cs`.
- Create: `CScore/Planar/PlanarBoundaryActionResolver.cs`.
- Create: `CScore/Planar/PlanarBoundaryFrameConverter.cs`.
- Create: `CScore/Planar/PlanarBoundaryActionFingerprint.cs`.
- Test: `CScore.Tests/Planar/PlanarBoundaryActionResolverTests.cs`.

### Step 1: Write failing provider/resolver tests

Add test-local providers that return one force or kinematic action in the
fragment frame. Cover:

```csharp
[Fact]
public void CombinedModeAcceptsDisjointDofs()
{
    var result = Resolve(
        PlanarBoundaryActionSourceMode.Combined,
        ParentForce(PlanarDofMask.UX),
        TemplateKinematic(PlanarDofMask.UZ));

    Assert.True(result.IsCalculable, Diagnostics(result));
    Assert.Equal(PlanarDofMask.UX | PlanarDofMask.UZ, result.CoveredDofs);
}

[Theory]
[InlineData(PlanarBoundaryActionKind.Force, PlanarBoundaryActionKind.Force)]
[InlineData(PlanarBoundaryActionKind.Kinematic, PlanarBoundaryActionKind.Kinematic)]
[InlineData(PlanarBoundaryActionKind.Force, PlanarBoundaryActionKind.Kinematic)]
public void CombinedModeRejectsOverlappingDofs(
    PlanarBoundaryActionKind first, PlanarBoundaryActionKind second)
{
    var result = Resolve(
        PlanarBoundaryActionSourceMode.Combined,
        ActionFrom("parent", first, PlanarDofMask.UX),
        ActionFrom("template", second, PlanarDofMask.UX));

    Assert.False(result.IsCalculable);
    Assert.Contains(result.Diagnostics, d => d.Code == "planar_boundary_source_dof_conflict");
}

[Fact]
public void TemplateProviderDoesNotTreatMissingDofAsZero()
{
    var result = new PlanarBoundaryTemplateProvider(template).Resolve(request);

    Assert.Equal(PlanarDofMask.UX, result.CoveredDofs);
    Assert.DoesNotContain(PlanarDofMask.UZ, result.CoveredDofs);
}
```

Add tests for local-to-global and global-to-fragment vector conversion, moment
translation `M + r × F`, incompatible units, missing parent result, and
non-converged nonlinear parent step.

Run focused resolver tests and expect FAIL before implementation.

### Step 2: Define the provider contract

In `IPlanarBoundaryActionProvider.cs` define:

```csharp
public interface IPlanarBoundaryActionProvider
{
    PlanarBoundaryActionProviderResult Resolve(PlanarBoundaryActionRequest request);
}
```

`PlanarBoundaryActionRequest` contains the target `PlanarCutInterface`, requested
subcase, source scenario identity, source frame, requested DOF mask and balance
options. `PlanarBoundaryActionProviderResult` contains source mode, normalized
actions, covered DOF, source references and diagnostics.

Do not add a raw `ShellResult` dependency to `CScore`; future OpenSees, LIRA and
SCAD adapters will implement the provider in their own projects.

### Step 3: Implement template provider and resolver

`PlanarBoundaryTemplateProvider` validates explicit samples, normalizes vectors
to the target `FragmentFrame`, preserves units/sign policy and returns uncovered
DOFs as uncovered. It must not emit zero-valued samples for omitted components.

`PlanarBoundaryActionResolver`:

- resolves only the explicitly requested `Parent`, `Template` or `Combined` mode;
- rejects duplicate interface IDs and invalid provider results;
- accepts disjoint DOF coverage in `Combined`;
- rejects any overlapping force/force, kinematic/kinematic or force/kinematic
  coverage, even when values are equal;
- keeps all source contributions in provenance;
- returns a blocking diagnostic for incomplete required coverage;
- never performs solver-specific mapping.

### Step 4: Implement deterministic fingerprints

`PlanarBoundaryActionFingerprint.Compute` must include interface geometry,
normal/frame, modes, source mode, units, sample values/order, interpolation
policy, source identities and balance options. Use invariant numeric formatting
and SHA-256 conventions already used by `PlanarGeometryFingerprint` and
`PlanarConnectionFingerprint`.

### Step 5: Run tests

Run:

```text
dotnet test CScore.Tests/CScore.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarBoundaryAction
```

Expected: all core and resolver tests pass.

## Task 3: Build Cut-Interface Mesh Mapping and Action Discretization

**Files:**

- Create: `CScore/Planar/PlanarCutInterfaceMeshMapping.cs`.
- Create: `CScore/Planar/PlanarBoundaryActionMeshMapper.cs`.
- Test: `CScore.Tests/Planar/PlanarBoundaryActionMeshMapperTests.cs`.

### Step 1: Write failing mapping tests

Use a rectangular snapshot with an ordered three-node interface chain and test:

- mapping from `PlanarConstraintMeshMapping.OrderedCurveEdges`;
- mapping from `PlanarMeshBoundaryMapping.NodeIndices`;
- reverse edge orientation normalized to increasing `s`;
- duplicate/unknown node and broken chain diagnostics;
- stale snapshot fingerprint diagnostic;
- linearly varying force preserves total force and first moment;
- constant moment-per-length is included in mapped nodal moments;
- kinematic samples interpolate to each interface node;
- missing interface coverage blocks the result.

Representative assertion:

```csharp
[Fact]
public void Map_LinearBoundaryForcePreservesForceAndMoment()
{
    var mapped = PlanarBoundaryActionMeshMapper.Map(
        interfaceDefinition, snapshot, actionSet, mapping);

    Assert.True(mapped.IsCalculable, Diagnostics(mapped));
    Assert.Equal(mapped.AppliedForceGlobal, mapped.MappedForceGlobal);
    Assert.Equal(mapped.AppliedMomentGlobal, mapped.MappedMomentGlobal);
}
```

Run the focused mapper tests and expect FAIL before implementation.

### Step 2: Implement mapping model

In `PlanarCutInterfaceMeshMapping.cs` add:

- `PlanarCutInterfaceMeshNode(NodeIndex, Position, S)`;
- `PlanarCutInterfaceMeshMapping` with interface ID, snapshot ID/fingerprint,
  ordered nodes, ordered `PlanarMeshEdge`, orientation, normal and diagnostics;
- `PlanarCutInterfaceMeshMapper.Map` that resolves either `MeshConstraintId` from
  `snapshot.ConstraintMappings` or `BoundaryKey` from `snapshot.BoundaryMappings`.

For constraint curves, reconstruct ordered nodes from `OrderedCurveEdges` and
verify edge continuity, endpoints, source geometry and tolerance. For boundary
chains, derive edges from adjacent mapped nodes. Do not search for nearest nodes.

### Step 3: Implement force integration

`PlanarBoundaryActionMeshMapper` returns:

- `PlanarNodalAction` per snapshot node with global force and moment;
- prescribed DOF assignments keyed by `(NodeIndex, Dof)`;
- preserved support DOFs;
- applied/mapped global force and moment;
- mapping and balance diagnostics.

For each ordered edge `[a,b]` of length `L`, evaluate endpoint samples and use
consistent linear integration:

```text
F_a = L * (2*q_a + q_b) / 6
F_b = L * (q_a + 2*q_b) / 6
M_a = L * (2*m_a + m_b) / 6
M_b = L * (m_a + 2*m_b) / 6
```

Transform action vectors from `FragmentFrame` to global before accumulation.
The applied moment includes the force first moment about the declared reference
point; the mapped moment sums `nodePosition × nodalForce + nodalMoment` about the
same global origin. Use the existing relative/absolute tolerance pattern from
`PlanarLoadMapper`.

### Step 4: Implement kinematic interpolation

For each mapped node, calculate `s` from cumulative edge length and interpolate
between the bracketing samples. Create one prescribed assignment per covered
DOF. Reject duplicate assignments, out-of-range `s`, and a prescribed value on
an already preserved/fixed DOF unless the values are explicitly compatible by
the contract.

### Step 5: Run tests

Run:

```text
dotnet test CScore.Tests/CScore.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarBoundaryActionMeshMapper
```

Expected: focused mesh mapping tests pass, including exact force/moment balance.

## Task 4: Expose Cut Loci to the Existing Gmsh Pipeline

**Files:**

- Modify: `CScore/Planar/PlanarCutInterface.cs` if the factory from Task 1 needs
  to attach structural metadata or a deterministic constraint ID.
- Test: `OpenCS.Gmsh.Tests/GmshPlanarGeoBuilderTests.cs`.
- Test: `OpenCS.Gmsh.Tests/GmshPlanarMesherTests.cs`.
- Test: `OpenCS.Gmsh.Tests/GmshCutInterfaceMappingTests.cs`.

### Step 1: Add deterministic `.geo` test

Create a rectangular region and a cut interface represented by a request-local
curve constraint. Assert that `GmshPlanarGeoBuilder.Build` emits:

```text
constraint:<id>:curve
Physical Curve
In Surface
```

The test must also assert that the cut curve is not emitted as a hole or a
second plane surface.

### Step 2: Add real Gmsh mapping test

Run `GmshPlanarMesher` with the cut constraint, using the fixed executable path
from `AGENTS.md`. Assert:

- calculable MSH 4.1 snapshot;
- exactly one matching `ConstraintMappings` entry;
- a non-empty ordered curve edge chain;
- continuous nodes covering the original cut curve;
- no shell elements are removed from the host region;
- artifact manifest remains present.

The test cleans its unique temporary artifact root in `finally` and skips only
when the standard `OpenCS.Gmsh` executable resolver reports that Gmsh is
unavailable.

### Step 3: Connect the generic mapper

Use the real `PlanarConstraintMeshMapping` from the snapshot as input to
`PlanarCutInterfaceMeshMapper`. Do not add another parser or another Gmsh tag
mapping table.

Run:

```text
dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore --filter FullyQualifiedName~GmshCutInterface
dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore --filter FullyQualifiedName~GmshPlanar
```

Expected: existing Gmsh tests and the new cut-locus tests pass.

## Task 5: Add OpenSees Prescribed DOF and Boundary Action Adapter

**Files:**

- Create: `OpenCS.OpenSees/Structural/ShellKinematicLoad.cs`.
- Modify: `OpenCS.OpenSees/Structural/ShellNonlinearStage.cs`.
- Modify: `OpenCS.OpenSees/Structural/ShellOpenSeesModel.cs`.
- Modify: `OpenCS.OpenSees/Tcl/ShellTclGenerator.cs`.
- Create: `OpenCS.OpenSees.CScore/PlanarBoundaryActionOpenSeesAdapter.cs`.
- Create: `OpenCS.OpenSees.CScore/PlanarBoundaryOpenSeesResult.cs`.
- Test: `OpenCS.OpenSees.Tests/ShellKinematicLoadTests.cs`.
- Test: `OpenCS.OpenSees.Tests/PlanarBoundaryActionOpenSeesAdapterTests.cs`.
- Test: `OpenCS.OpenSees.Tests/PlanarBoundaryActionOpenSeesIntegrationTests.cs`.

### Step 1: Write failing shell model/generator tests

Add tests that assert:

```csharp
[Fact]
public void Validate_RejectsKinematicDofOverFixedNode()
{
    var model = MinimalShellModelWithFixedNode() with
    {
        Stages = [new ShellNonlinearStage
        {
            Tag = "parent-kinematic",
            KinematicLoads = [new ShellKinematicLoad(1, 1, 0.01)]
        }]
    };

    var exception = Assert.Throws<InvalidOperationException>(() => model.Validate());

    Assert.Contains("kinematic", exception.Message, StringComparison.OrdinalIgnoreCase);
}

[Fact]
public void Generate_EmitsSpForKinematicLoadAndNotFixForIt()
{
    var model = MinimalShellModelWithFreeNode() with
    {
        Stages = [new ShellNonlinearStage
        {
            Tag = "parent-kinematic",
            KinematicLoads = [new ShellKinematicLoad(1, 3, 0.01)]
        }]
    };

    var script = new ShellTclGenerator().Generate(model);

    Assert.Contains("sp 1 3", script);
    Assert.Contains("fix 1 0 0 0 0 0 0", script);
}
```

The test must also verify kinematic nodes are included in reaction recorder
tags and duplicate stage DOF assignments are rejected.

Run the focused OpenSees tests and expect FAIL before implementation.

### Step 2: Extend normalized shell stages

Add `ShellKinematicLoad(NodeTag, Dof, Value)` with finite-value validation.
Extend `ShellNonlinearStage` with `KinematicLoads`.

Extend `ShellOpenSeesModel.Validate` to:

- require node tags to exist;
- require DOF in `1..6`;
- reject non-finite values;
- reject duplicate `(NodeTag, Dof)` within one stage;
- reject any kinematic DOF that intersects `NormalizedShellNode.Fixed`, even
  when the value happens to be zero, to avoid duplicate solver constraints.

### Step 3: Emit Tcl prescribed DOF

In `ShellTclGenerator`:

- retain the existing `fix` command for `Fixed` only;
- emit each stage's force loads and then `sp node dof value` inside that stage's
  `pattern Plain` block;
- include fixed and kinematic nodes in `restrainedTags` for reaction recording;
- preserve deterministic ordering by stage, node tag and DOF.

Do not emit `sp` outside the stage pattern and do not convert it to `fix`.

### Step 4: Implement OpenSees action adapter

`PlanarBoundaryActionOpenSeesAdapter.Apply` accepts:

- validated `ShellOpenSeesModel`;
- `PlanarBoundaryActionMeshMappingResult`;
- snapshot-index to OpenSees-tag map from
  `PlanarMeshShellModelResult.NodeIndexToTag`;
- target stage index/tag.

It returns `PlanarBoundaryOpenSeesResult` with model, mapped provenance and
diagnostics. The adapter:

- rejects non-calculable input;
- adds global force/moment nodal loads to the target stage;
- applies `preserve_support` by cloning the relevant node `Fixed` mask;
- converts prescribed assignments to `ShellKinematicLoad`;
- rejects unknown snapshot nodes, missing stages and duplicate/conflicting DOF;
- leaves section/material/element tags untouched.

### Step 5: Add real OpenSees integration coverage

Build a small elastic vertical wall model from an actual Gmsh snapshot or the
existing `GmshOpenSeesPatchTestFixture`, then:

1. map a uniform force action on an ordered cut boundary;
2. run through `ShellTclGenerator` and the real
   `C:\Tools\OpenSees\bin\OpenSees.exe`;
3. assert completed status, finite displacements and reaction balance;
4. run a separate prescribed-displacement action;
5. assert the mapped interface nodes reach the prescribed value and no
   artificial fixed-zero constraint was emitted.

Use unique temporary artifact directories and clean them in `finally`.

Run:

```text
dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --no-restore --filter FullyQualifiedName~PlanarBoundaryActionOpenSees
dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --no-restore --filter FullyQualifiedName~ShellKinematicLoad
```

Expected: focused unit and real-process integration tests pass.

## Task 6: Add CSfea Boundary Action Adapter

**Files:**

- Create: `CSfea.CScore/PlanarBoundaryActionShellMeshAdapter.cs`.
- Create: `CSfea.CScore/PlanarBoundaryShellMeshResult.cs`.
- Test: `CSfea.Tests/PlanarBoundaryActionShellMeshAdapterTests.cs`.
- Modify: `CSfea.Tests/Program.cs`.

### Step 1: Add hand-rolled failing tests

Follow the existing `TestHarness` convention. Add tests for:

- force/moment components at global DOF offsets `0..2` and `3..5`;
- force balance against the CScore mapping result;
- `preserve_support` producing fixed zero DOFs;
- kinematic actions producing matching `fixedDofs` and `uFixed` values;
- duplicate/conflicting DOF diagnostics;
- rejection of `IsCalculable=false` mapping.

Example shape:

```csharp
public static void Apply_KinematicActionUsesNonzeroUFixed()
{
    var result = PlanarBoundaryActionShellMeshAdapter.Apply(
        Mesh(), CalculableKinematicMapping());

    TestHarness.Check(
        "PlanarBoundaryActionShellMeshAdapter_uFixed",
        result.UFixed.Contains(0.01),
        "prescribed displacement was lost or converted to fixed zero");
}
```

Register `PlanarBoundaryActionShellMeshAdapterTests.RunAll()` in `CSfea.Tests/Program.cs` next to the existing planar adapter tests.

### Step 2: Implement result and adapter

`PlanarBoundaryShellMeshResult` contains `IsCalculable`, full `NodalForceVector`,
sorted `FixedDofs`, matching `UFixed`, diagnostics and source provenance.

`PlanarBoundaryActionShellMeshAdapter.Apply`:

- validates `ShellMesh.DofsPerNode == 6`;
- rejects non-calculable mapping;
- converts snapshot node indices to global shell DOF offsets;
- adds force to offsets `0,1,2` and moment to offsets `3,4,5`;
- merges repeated nodal actions by addition;
- adds preserved support values as zero;
- adds kinematic values as nonzero `uFixed` entries;
- rejects conflicting fixed/prescribed values and unknown node indices;
- returns sorted DOFs and values in the same order.

### Step 3: Run focused and baseline harnesses

Run:

```text
dotnet build OpenCS.sln --no-restore
dotnet run --project CSfea.Tests --no-build --no-restore
```

Expected: new adapter checks pass. Existing unrelated CSfea harness failures,
if present, must remain at the baseline count and be reported separately.

## Task 7: Complete Cross-Layer Tests and Verification

**Files:**

- Modify only the focused test files from Tasks 1–6 if a test exposes a real
  contract defect.
- No UI or persistence files.

### Step 1: Run all affected project tests

Run sequentially where SQLite cleanup is involved:

```text
dotnet test CScore.Tests/CScore.Tests.csproj --no-restore
dotnet test OpenCS.Gmsh.Tests/OpenCS.Gmsh.Tests.csproj --no-restore
dotnet test OpenCS.OpenSees.Tests/OpenCS.OpenSees.Tests.csproj --no-restore
```

The full OpenSees run may reproduce the known parallel SQLite cleanup race. If
it does, run each failed test in isolation and record the result; do not modify
unrelated database tests in this feature.

### Step 2: Run solution build and harness

```text
dotnet build OpenCS.sln --no-restore
dotnet run --project CSfea.Tests --no-build --no-restore
```

The solution build must have zero errors. Existing warnings about the absent
`OpenCS.Core.UI` project and pre-existing CSfea harness failures are not part of
this feature; the new focused tests must pass.

### Step 3: Review implementation against the approved spec

Check explicitly:

- no nearest-node fallback;
- no force/kinematic automatic combination;
- no nonzero `sp` encoded by `fix`;
- no Gmsh/OpenSees tag leakage into CScore domain IDs;
- all blocking diagnostics prevent solver invocation;
- all force/moment conversions record frame, units and reference point;
- all new public types have Russian XML documentation;
- no files outside the approved scope changed.

### Step 4: Inspect final worktree

Run:

```text
git status -sb
```

Do not commit or push unless the user explicitly requests it.
