using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;

namespace Hotfix;

// 림월드 바닐라에서 제공하는 기능으로는 record를 읽을 수 없음
internal static class XmlHelper
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> PropertyCache = new();
    private static readonly ConcurrentDictionary<(Type, string), Type> ClassCache = new();

    public static void Save(string path, object value)
    {
        using var buffer = new MemoryStream();
        using (var writer = XmlWriter.Create(buffer, new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "\t",
            CheckCharacters = false,
            NewLineHandling = NewLineHandling.Entitize,
        }))
            new XDocument(Write(value.GetType().Name, value, value.GetType())).Save(writer);
        File.WriteAllBytes(path, buffer.ToArray());
    }

    public static T? Load<T>(string path) where T : class
    {
        using var reader = XmlReader.Create(path, new XmlReaderSettings { CheckCharacters = false });
        return (T?)Read(XDocument.Load(reader, LoadOptions.PreserveWhitespace).Root!, typeof(T));
    }

    private static XElement Write(string name, object value, Type declared)
    {
        var element = new XElement(name);
        if (Text(value) is { } text)
        {
            element.Value = text;
        }
        else if (value is IEnumerable items)
        {
            var itemType = declared.GetTypeOfListItem();
            foreach (var item in items)
                element.Add(Write("li", item, itemType));
        }
        else
        {
            var type = value.GetType();
            if (type != declared)
                element.SetAttributeValue("Class", type.Name);
            foreach (var property in Properties(type))
            {
                if (property.GetValue(value) is { } child)
                    element.Add(Write(Camel(property.Name), child, property.PropertyType));
            }
        }
        return element;
    }

    private static object? Read(XElement element, Type declared)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (type == typeof(string))
            return element.Value;
        if (type == typeof(bool))
            return bool.Parse(element.Value);
        if (type.IsEnum)
            return Enum.Parse(type, element.Value);
        if (type == typeof(DateTime))
            return DateTime.Parse(element.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        if (type == typeof(TimeSpan))
            return TimeSpan.Parse(element.Value, CultureInfo.InvariantCulture);
        if (type.IsPrimitive)
            return Convert.ChangeType(element.Value, type, CultureInfo.InvariantCulture);
        if (typeof(IEnumerable).IsAssignableFrom(type))
        {
            var itemType = type.GetTypeOfListItem();
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(itemType))!;
            foreach (var item in element.Elements("li"))
                list.Add(Read(item, itemType));
            return list;
        }

        if (element.Attribute("Class")?.Value is { } name)
            type = ClassCache.GetOrAdd((type, name), static key =>
                key.Item1.Assembly.GetTypes().First(candidate =>
                    candidate.Name == key.Item2 && key.Item1.IsAssignableFrom(candidate)));
        // 매개변수가 가장 많은 생성자가 레코드의 기본 생성자
        var constructor = type.GetConstructors().OrderByDescending(candidate => candidate.GetParameters().Length)
            .First();
        var parameters = constructor.GetParameters();
        var value = constructor.Invoke(parameters.Select(parameter =>
            element.Element(Camel(parameter.Name!)) is { } child
                ? Read(child, parameter.ParameterType)
                : parameter.HasDefaultValue
                    ? parameter.DefaultValue
                    : null).ToArray());
        foreach (var property in Properties(type))
        {
            if (!parameters.Any(parameter => parameter.Name == property.Name) && element.Element(Camel(property.Name)) is { } child)
                property.SetValue(value, Read(child, property.PropertyType));
        }
        return value;
    }

    private static string? Text(object value) => value switch
    {
        string text => text,
        bool flag => flag ? "True" : "False",
        Enum => value.ToString(),
        DateTime time => time.ToString("s", CultureInfo.InvariantCulture),
        TimeSpan span => span.ToString("c", CultureInfo.InvariantCulture),
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        float number => number.ToString("R", CultureInfo.InvariantCulture),
        IFormattable number when value.GetType().IsPrimitive => number.ToString(null, CultureInfo.InvariantCulture),
        _ => null,
    };

    private static PropertyInfo[] Properties(Type type) => PropertyCache.GetOrAdd(type, static type =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead && property.SetMethod is { IsPublic: true } &&
                               property.GetIndexParameters().Length == 0
                               && !property.IsDefined(typeof(XmlIgnoreAttribute))).ToArray());

    private static Type GetTypeOfListItem(this Type list) => new[] { list }.Concat(list.GetInterfaces())
                                                   .FirstOrDefault(type =>
                                                       type.IsGenericType && type.GetGenericTypeDefinition() ==
                                                       typeof(IEnumerable<>))?.GetGenericArguments()[0]
                                               ?? typeof(object);

    private static string Camel(string name) => char.ToLowerInvariant(name[0]) + name[1..];
}
