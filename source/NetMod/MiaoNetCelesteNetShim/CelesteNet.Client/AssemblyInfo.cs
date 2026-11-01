using System.Reflection;

// Tells MiaoNet that this assembly provides another mod's name rather than being a client of its
// own. MiaoNet warns when it finds a module registered under a name it also serves, so a
// compatibility shim has to be able to say what it is -- and what it speaks is MiaoNet's own
// protocol, so warning about it would be backwards. The value names the mod being stood in for.
[assembly: AssemblyMetadata("MiaoNetCompatShim", "CelesteNet.Client")]
