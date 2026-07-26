namespace OpenCS.OpenSees.Structural;

/// <summary>Тип OpenSees rigidLink.</summary>
public enum ShellRigidLinkType { Bar, Beam }

/// <summary>Жёсткая связь rigidLink между двумя узлами shell/beam модели.</summary>
public sealed record ShellRigidLinkConstraint(int MasterNode, int SlaveNode, ShellRigidLinkType Type);
