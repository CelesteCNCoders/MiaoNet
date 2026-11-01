using System.Reflection;
using System.Runtime.CompilerServices;
using Celeste.Mod.CelesteNet.DataTypes;

namespace Celeste.Mod.CelesteNet;

public sealed class DataContext : IDisposable
{
    public readonly Dictionary<string, Type> IDToDataType = new(StringComparer.Ordinal);
    public readonly Dictionary<Type, string> DataTypeToID = new();
    public readonly Dictionary<Type, string> DataTypeToSource = new();
    public readonly Dictionary<string, Type> IDToMetaType = new(StringComparer.Ordinal);
    public readonly Dictionary<Type, string> MetaTypeToID = new();

    private readonly Dictionary<Type, DataHandler> handlers = new();
    private readonly Dictionary<Type, DataFilter> filters = new();
    private readonly Dictionary<object, List<(Type Type, DataHandler Handler)>> registeredHandlers = new();
    private readonly Dictionary<object, List<(Type Type, DataFilter Filter)>> registeredFilters = new();
    private readonly Dictionary<uint, DataType> refs = new();

    /// <summary>
    /// Scans every assembly that is loaded right now. Mods are loaded after the shim, so this
    /// is re-run whenever the loaded assembly count changes.
    /// </summary>
    public void RescanAllDataTypes()
        => RescanDataTypes(GetLoadedTypes());

    public static Type[] GetLoadedTypes()
    {
        List<Type> types = [];
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                types.AddRange(assembly.GetTypes());
            }
            catch (ReflectionTypeLoadException exception)
            {
                types.AddRange(exception.Types.OfType<Type>());
            }
            catch
            {
                // Dynamic / reflection-only assemblies can refuse enumeration; they never carry data types.
            }
        }
        return [.. types];
    }

    public void RescanDataTypes(Type[] types)
    {
        foreach (Type type in types)
        {
            if (type.IsAbstract)
                continue;

            if (typeof(DataType).IsAssignableFrom(type))
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                FieldInfo? idField = FindStaticField(type, "DataID");
                FieldInfo? sourceField = FindStaticField(type, "DataSource");
                string? id = idField?.GetValue(null) as string;
                string? source = sourceField?.GetValue(null) as string;
                if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(source) || IDToDataType.ContainsKey(id))
                    continue;

                IDToDataType[id] = type;
                DataTypeToID[type] = id;
                DataTypeToSource[type] = source;
            }
            else if (typeof(MetaType).IsAssignableFrom(type))
            {
                RuntimeHelpers.RunClassConstructor(type.TypeHandle);
                string? id = FindStaticField(type, "MetaID")?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(id) && !IDToMetaType.ContainsKey(id))
                {
                    IDToMetaType[id] = type;
                    MetaTypeToID[type] = id;
                }
            }
        }
    }

    public DataHandler RegisterHandler<T>(DataHandler<T> handler) where T : DataType<T>
    {
        DataHandler wrapper = (connection, data) => handler(connection, (T)data);
        RegisterHandler(typeof(T), wrapper);
        return wrapper;
    }

    public void RegisterHandler(Type type, DataHandler handler)
        => handlers[type] = handlers.TryGetValue(type, out DataHandler? existing) ? existing + handler : handler;

    public void RegisterFilter(Type type, DataFilter filter)
        => filters[type] = filters.TryGetValue(type, out DataFilter? existing) ? existing + filter : filter;

    public void RegisterHandlersIn(object owner)
    {
        if (registeredHandlers.ContainsKey(owner))
            return;

        List<(Type, DataHandler)> ownerHandlers = [];
        List<(Type, DataFilter)> ownerFilters = [];
        registeredHandlers[owner] = ownerHandlers;
        registeredFilters[owner] = ownerFilters;

        foreach (MethodInfo method in owner.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (method.Name is not "Handle" and not "Filter")
                continue;
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2 || !typeof(CelesteNetConnection).IsAssignableFrom(parameters[0].ParameterType) ||
                !typeof(DataType).IsAssignableFrom(parameters[1].ParameterType))
                continue;

            Type dataType = parameters[1].ParameterType;
            if (method.Name == "Handle")
            {
                DataHandler handler = (connection, data) => method.Invoke(owner, [connection, data]);
                ownerHandlers.Add((dataType, handler));
                RegisterHandler(dataType, handler);
            }
            else if (method.ReturnType == typeof(bool))
            {
                DataFilter filter = (connection, data) => method.Invoke(owner, [connection, data]) as bool? ?? false;
                ownerFilters.Add((dataType, filter));
                RegisterFilter(dataType, filter);
            }
        }
    }

    public void UnregisterHandlersIn(object owner)
    {
        if (registeredHandlers.TryGetValue(owner, out List<(Type Type, DataHandler Handler)>? ownerHandlers))
        {
            foreach ((Type type, DataHandler handler) in ownerHandlers)
            {
                if (!handlers.TryGetValue(type, out DataHandler? registeredHandler))
                    continue;

                DataHandler? remaining = registeredHandler - handler;
                if (remaining is null)
                    handlers.Remove(type);
                else
                    handlers[type] = remaining;
            }
            registeredHandlers.Remove(owner);
        }

        if (registeredFilters.TryGetValue(owner, out List<(Type Type, DataFilter Filter)>? ownerFilters))
        {
            foreach ((Type type, DataFilter filter) in ownerFilters)
            {
                if (!filters.TryGetValue(type, out DataFilter? registeredFilter))
                    continue;

                DataFilter? remaining = registeredFilter - filter;
                if (remaining is null)
                    filters.Remove(type);
                else
                    filters[type] = remaining;
            }
            registeredFilters.Remove(owner);
        }
    }

    public void Handle(CelesteNetConnection connection, DataType data)
    {
        if (filters.TryGetValue(data.GetType(), out DataFilter? filter) && !filter(connection, data))
            return;

        // Refs must be populated before handlers run: mods resolve other players through them.
        if (data is DataPlayerInfo playerInfo && playerInfo.ID != uint.MaxValue)
            RegisterRef(playerInfo.ID, playerInfo);

        if (handlers.TryGetValue(data.GetType(), out DataHandler? handler))
            handler(connection, data);
    }

    public void RegisterRef(uint id, DataType data)
        => refs[id] = data;

    public void UnregisterRef(uint id)
        => refs.Remove(id);

    public IEnumerable<T> GetRefs<T>() where T : DataType
        => refs.Values.OfType<T>();

    public T Get<T>(uint id) where T : DataType<T>, new()
    {
        if (TryGetRef(id, out T? value) && value is not null)
            return value;
        throw new KeyNotFoundException($"No reference registered for id {id}.");
    }

    public bool TryGetRef<T>(uint id, out T? value) where T : DataType<T>, new()
    {
        if (refs.TryGetValue(id, out DataType? data) && data is T typed)
        {
            value = typed;
            return true;
        }

        value = null;
        return false;
    }

    /// <summary>
    /// Meta types are written as <c>[id string][length byte][payload]</c>. The length lets the
    /// reader skip a meta type this side does not know about.
    /// </summary>
    public MetaType ReadMeta(CelesteNetBinaryReader reader)
    {
        string id = reader.ReadString();
        int length = reader.ReadByte();
        long start = reader.BaseStream.Position;

        if (!IDToMetaType.TryGetValue(id, out Type? type))
        {
            return new MetaUnparsed
            {
                InnerID = id,
                InnerData = reader.ReadBytes(length),
            };
        }

        if (Activator.CreateInstance(type) is not MetaType meta)
            throw new InvalidOperationException($"Cannot create instance of MetaType '{type.FullName}'");

        meta.Read(reader);

        long consumed = reader.BaseStream.Position - start;
        if (consumed < length)
            reader.BaseStream.Seek(length - consumed, SeekOrigin.Current);

        return meta;
    }

    public int WriteMeta(CelesteNetBinaryWriter writer, MetaType meta)
    {
        long start = writer.BaseStream.Position;
        writer.Write(meta.GetTypeID(this));

        long lengthPosition = writer.BaseStream.Position;
        writer.Write((byte)0);
        meta.Write(writer);

        long end = writer.BaseStream.Position;
        long length = end - lengthPosition - 1;
        if (length > byte.MaxValue)
            throw new InvalidOperationException($"MetaType payload of {length} bytes is too large.");

        writer.BaseStream.Position = lengthPosition;
        writer.Write((byte)length);
        writer.BaseStream.Position = end;

        return (int)(end - start);
    }

    public string GetTypeID(DataType data) => data.GetTypeID(this);

    public void Dispose()
    {
        handlers.Clear();
        filters.Clear();
        registeredHandlers.Clear();
        registeredFilters.Clear();
        refs.Clear();
    }

    private static FieldInfo? FindStaticField(Type type, string name)
    {
        for (Type? current = type; current is not null && current != typeof(object); current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name, BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly);
            if (field is not null)
                return field;
        }
        return null;
    }
}
