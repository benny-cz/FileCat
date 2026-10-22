using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace FileCat.App.Tests;

/// <summary>
/// FileCat compiles against Avalonia's implementation assemblies so that its window can provide the root focus
/// contract screen readers use (DescendantFocusWindowAutomationPeer). Every other Avalonia private API stays
/// unavailable: this inspects FileCat.dll's metadata and fails on any other referenced private type or member,
/// including an overload that only became visible through that build setting.
/// </summary>
public sealed class PrivateAvaloniaApiTests
{
    private static readonly HashSet<string> Allowed =
    [
        "Avalonia.Automation.Provider.IRootProvider",
        "Avalonia.Automation.Provider.IRootProvider.GetFocus",
        "Avalonia.Automation.Provider.IRootProvider.GetPeerFromPoint",
        "Avalonia.Automation.Provider.IRootProvider.get_PlatformImpl",
        "Avalonia.Automation.Provider.IRootProvider.add_FocusChanged",
        "Avalonia.Automation.Provider.IRootProvider.remove_FocusChanged",
        // The declared type of IRootProvider.PlatformImpl.
        "Avalonia.Platform.ITopLevelImpl",
    ];

    [Fact]
    public void FileCat_uses_no_Avalonia_private_api_beyond_the_root_focus_contract()
    {
        var app = typeof(FileCat.App.Controls.DescendantFocusWindowAutomationPeer).Assembly;
        using var stream = File.OpenRead(app.Location);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var used = new SortedSet<string>(StringComparer.Ordinal);
        var unresolved = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var handle in md.TypeReferences)
            if (Resolve(md, handle) is { } type && IsPrivate(type))
                used.Add(Name(type));

        var provider = new NameProvider();
        var definitions = new DefinitionProvider();
        foreach (var handle in md.MemberReferences)
        {
            var member = md.GetMemberReference(handle);
            var type = member.Parent.Kind switch
            {
                HandleKind.TypeReference => Resolve(md, (TypeReferenceHandle)member.Parent),
                // A member of a constructed generic type: its open definition declares the member.
                HandleKind.TypeSpecification => md.GetTypeSpecification((TypeSpecificationHandle)member.Parent).DecodeSignature(definitions, null),
                _ => null,
            };
            if (type is null) continue;
            string name = md.GetString(member.Name);
            if (member.GetKind() == MemberReferenceKind.Field)
            {
                if (type.GetField(name, All) is { } field && IsPrivate(field)) used.Add(Name(type) + "." + name);
                continue;
            }
            var signature = member.DecodeMethodSignature(provider, null);
            var candidates = type.GetMember(name, MemberTypes.Method | MemberTypes.Constructor, All)
                .OfType<MethodBase>()
                .Where(m => m.GetParameters().Select(p => Normalize(p.ParameterType)).SequenceEqual(signature.ParameterTypes))
                .ToArray();
            if (candidates.Length == 0) unresolved.Add(Name(type) + "." + name + "(" + string.Join(",", signature.ParameterTypes) + ")");
            if (candidates.Any(m => IsPrivate(m) || IsPrivate(Accessor(m))))
                used.Add(Name(type) + "." + name);
        }

        Assert.DoesNotContain(used, name => !Allowed.Contains(name));
        Assert.Empty(unresolved);
        // The screen-reader root contract itself is still the private interface the bridges consume.
        Assert.Contains("Avalonia.Automation.Provider.IRootProvider.add_FocusChanged", used);
    }

    private const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                     BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static Type? Resolve(MetadataReader md, TypeReferenceHandle handle)
    {
        var reference = md.GetTypeReference(handle);
        if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
            return Resolve(md, (TypeReferenceHandle)reference.ResolutionScope)?.GetNestedType(md.GetString(reference.Name), All);
        if (reference.ResolutionScope.Kind != HandleKind.AssemblyReference) return null;
        string assembly = md.GetString(md.GetAssemblyReference((AssemblyReferenceHandle)reference.ResolutionScope).Name);
        if (!assembly.StartsWith("Avalonia", StringComparison.Ordinal)) return null;
        string ns = md.GetString(reference.Namespace);
        return Type.GetType($"{ns}.{md.GetString(reference.Name)}, {assembly}", throwOnError: true);
    }

    private static bool IsPrivate(MemberInfo? member)
    {
        for (var current = member; current is not null; current = current.DeclaringType)
            if (current.GetCustomAttributes(inherit: false).Any(a => a.GetType().FullName == "Avalonia.Metadata.PrivateApiAttribute"))
                return true;
        return false;
    }

    /// <summary>The property or event an accessor belongs to, whose attributes also hide the accessor.</summary>
    private static MemberInfo? Accessor(MethodBase method)
    {
        if (method is not MethodInfo info || method.DeclaringType is not { } type) return null;
        return type.GetProperties(All).FirstOrDefault(p => p.GetMethod == info || p.SetMethod == info) as MemberInfo ??
               type.GetEvents(All).FirstOrDefault(e => e.AddMethod == info || e.RemoveMethod == info);
    }

    private static string Name(Type type) => (type.FullName ?? type.Name).Replace('+', '.');

    private static string Normalize(Type type)
    {
        if (type.IsByRef) return Normalize(type.GetElementType()!) + "&";
        if (type.IsArray) return Normalize(type.GetElementType()!) + "[]";
        if (type.IsGenericParameter) return (type.DeclaringMethod is null ? "!" : "!!") + type.GenericParameterPosition;
        if (type.IsGenericType)
            return Name(type.GetGenericTypeDefinition()) + "<" + string.Join(",", type.GetGenericArguments().Select(Normalize)) + ">";
        return Name(type);
    }

    /// <summary>Resolves an Avalonia generic instantiation to its open definition; other types are not inspected.</summary>
    private sealed class DefinitionProvider : ISignatureTypeProvider<Type?, object?>
    {
        public Type? GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Resolve(reader, handle);
        public Type? GetGenericInstantiation(Type? genericType, ImmutableArray<Type?> typeArguments) => genericType;
        public Type? GetPrimitiveType(PrimitiveTypeCode typeCode) => null;
        public Type? GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => null;
        public Type? GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) => null;
        public Type? GetSZArrayType(Type? elementType) => null;
        public Type? GetArrayType(Type? elementType, ArrayShape shape) => null;
        public Type? GetByReferenceType(Type? elementType) => null;
        public Type? GetPointerType(Type? elementType) => null;
        public Type? GetGenericTypeParameter(object? context, int index) => null;
        public Type? GetGenericMethodParameter(object? context, int index) => null;
        public Type? GetFunctionPointerType(MethodSignature<Type?> signature) => null;
        public Type? GetModifiedType(Type? modifier, Type? unmodifiedType, bool isRequired) => unmodifiedType;
        public Type? GetPinnedType(Type? elementType) => null;
    }

    /// <summary>Decodes signature types to the same normalized names as <see cref="Normalize"/>.</summary>
    private sealed class NameProvider : ISignatureTypeProvider<string, object?>
    {
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode switch
        {
            PrimitiveTypeCode.Boolean => "System.Boolean", PrimitiveTypeCode.Byte => "System.Byte",
            PrimitiveTypeCode.Char => "System.Char", PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.Int16 => "System.Int16", PrimitiveTypeCode.Int32 => "System.Int32",
            PrimitiveTypeCode.Int64 => "System.Int64", PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.Object => "System.Object", PrimitiveTypeCode.SByte => "System.SByte",
            PrimitiveTypeCode.Single => "System.Single", PrimitiveTypeCode.String => "System.String",
            PrimitiveTypeCode.TypedReference => "System.TypedReference", PrimitiveTypeCode.UInt16 => "System.UInt16",
            PrimitiveTypeCode.UInt32 => "System.UInt32", PrimitiveTypeCode.UInt64 => "System.UInt64",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr", PrimitiveTypeCode.Void => "System.Void",
            _ => typeCode.ToString(),
        };
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
        {
            var definition = reader.GetTypeDefinition(handle);
            string name = reader.GetString(definition.Name);
            return definition.GetDeclaringType() is { IsNil: false } outer
                ? GetTypeFromDefinition(reader, outer, rawTypeKind) + "." + name
                : reader.GetString(definition.Namespace) + "." + name;
        }
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            string name = reader.GetString(reference.Name);
            return reference.ResolutionScope.Kind == HandleKind.TypeReference
                ? GetTypeFromReference(reader, (TypeReferenceHandle)reference.ResolutionScope, rawTypeKind) + "." + name
                : reader.GetString(reference.Namespace) + "." + name;
        }
        public string GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        public string GetSZArrayType(string elementType) => elementType + "[]";
        public string GetArrayType(string elementType, ArrayShape shape) => elementType + "[" + new string(',', shape.Rank - 1) + "]";
        public string GetByReferenceType(string elementType) => elementType + "&";
        public string GetPointerType(string elementType) => elementType + "*";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            genericType + "<" + string.Join(",", typeArguments) + ">";
        public string GetGenericTypeParameter(object? context, int index) => "!" + index;
        public string GetGenericMethodParameter(object? context, int index) => "!!" + index;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
    }
}
