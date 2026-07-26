namespace OpenCS.OpenSees.Structural;

/// <summary>Кинематическая связь equalDOF между двумя узлами shell/beam модели.</summary>
public sealed record ShellEqualDofConstraint(int MasterNode, int SlaveNode, IReadOnlyList<int> Dofs);
