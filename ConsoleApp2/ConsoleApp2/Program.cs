using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq.Expressions;

namespace ConsoleApp2
{
    namespace Challenges
    {
        public interface ICloningService
        {
            T Clone<T>(T source);
        }

        public interface ICloneMaker
        {
            T Clone<T>(T source, CloningMode mode);
        }

        public class CloningService : ICloningService
        {
            private readonly Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> _typeMembers = new Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>>();
            private readonly Dictionary<object, object> _cloned = new Dictionary<object, object>(EqualityComparer<object>.Default);
            

            public T Clone<T>(T source)
            {
                try
                {
                    return CloneMakerFactory.GetCloneMaker(source, CloningMode.Deep, _typeMembers, _cloned).Clone(source, CloningMode.Deep);
                }
                catch (ArgumentException ex)
                {
                    //log it
                    throw;
                }
            }            
        }

        public class CloneMakerFactory
        {
            private static readonly HashSet<Type> simpleTypes = new HashSet<Type>() { typeof(bool), typeof(char), typeof(int), typeof(long), typeof(float), typeof(double) };
            private static readonly Dictionary<Type, ICloneMaker> _typeMakers = new Dictionary<Type, ICloneMaker>();

            public static ICloneMaker GetCloneMaker<T>(T source, CloningMode mode, Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> typeMembers,
                                                        Dictionary<Object, Object> cloned)
            {
                var type = source.GetType();
                if (_typeMakers.TryGetValue(type, out var maker)) return maker;

                if (simpleTypes.Contains(type)) return new DefaultCloneMaker();
                if (source is string str) return new StringCloneMaker();
                if (type.IsArray) return new ArrayCloneMaker(type, typeMembers, cloned);
                if (type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ICollection<>))) return new CollectionCloneMaker(type, typeMembers, cloned);

                switch (mode)
                {
                    case CloningMode.Deep:
                        return new DeepCloneMaker(type, typeMembers, cloned);
                    case CloningMode.Shallow:
                        return new DefaultCloneMaker();
                    default:
                        throw new ArgumentException("Deep or shallow mode should be defined.", nameof(mode));
                }
            }
        }

        public abstract class CloneMaker : ICloneMaker
        {
            public T Clone<T>(T source, CloningMode mode)
            {
                if (source == null) return source;
                return CloneInternal(source, mode);
            }
            protected abstract T CloneInternal<T>(T source, CloningMode mode);
        }

        public abstract class ComplexCloneMaker : CloneMaker
        {
            protected readonly Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> _typeMembers;
            protected readonly Dictionary<object, object> _cloned;
            protected readonly Type _type;

            public ComplexCloneMaker(Type type, Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> typeMembers, Dictionary<object, object> cloned)
            {
                _type = type;
                _typeMembers = typeMembers;
                _cloned = cloned;
            }

            protected override T CloneInternal<T>(T source, CloningMode mode)
            {
                if (_cloned.TryGetValue(source, out var val)) return (T)val;
                return CloneComplex(source, mode);
            }

            protected abstract T CloneComplex<T>(T source, CloningMode mode);
        }

        public class DeepCloneMaker : ComplexCloneMaker
        {
            private static Dictionary<Type, Dictionary<string, Func<object, object>>> dGets =  new Dictionary<Type, Dictionary<string, Func<object, object>>>();

            private static Dictionary<Type,Dictionary<string, Action<object, object>>> dSets = new Dictionary<Type, Dictionary<string, Action<object, object>>>();
            
            static Dictionary<Type, Delegate> _cachedConstrIL = new Dictionary<Type, Delegate>();            

            public DeepCloneMaker(Type type, Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> typeMembers, Dictionary<Object, Object> cloned)
                        : base(type, typeMembers, cloned)
            {
            }

            protected override T CloneComplex<T>(T source, CloningMode mode)
            {
                if (!_typeMembers.ContainsKey(_type))
                {
                    var fields = _type.GetFields(BindingFlags.Instance | BindingFlags.Public).Where(p => (p.Attributes & FieldAttributes.InitOnly) == 0).ToList();
                    var properties = _type.GetProperties(BindingFlags.Instance | BindingFlags.Public).Where(p => p.CanRead && p.CanWrite).ToList();
                    _typeMembers.Add(_type, GetCloneMode(((IEnumerable<MemberInfo>)fields).Concat(properties)).ToList());
                }
                
                return CloneWithMembers(source, _typeMembers[_type]);
            }

            private T CloneWithMembers<T>(T source, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>> members)
            {
                var copy = CreateInstance(source);
                _cloned.Add(source, copy);

                foreach (var member in members)
                {
                    if (member.Item3 == CloningMode.Ignore) continue;

                    Func<object> valueGetter;
                    Action<object, object> valueSetter;
                    Func<object> structValueGetter;
                    Action<object, object> structValueSetter;                    
                    switch (member.Item2)
                    {
                        case MemberTypes.Property:
                            var property = member.Item1 as PropertyInfo;
                            valueGetter = () => CreatePropGetter<T, object>(property)(source);
                            valueSetter = (target, value) => CreatePropSetter<T, object>(property)((T)target, value);
                            structValueGetter = () => property.GetValue(source);
                            structValueSetter = (target, value) => property.SetValue(target, value);                            
                            break;
                        case MemberTypes.Field:
                            var field = member.Item1 as FieldInfo;
                            valueGetter = () => CreateGetter<T, object>(field)(source);
                            valueSetter = (target, value) => CreateSetter<T, object>(field)((T)target, value);
                            structValueGetter = () => field.GetValue(source);
                            structValueSetter = (target, value) => field.SetValue(target, value);                            
                            break;
                        default:
                            throw new ArgumentException("Only filed and property cloning supported.");
                    }

                    object sourceValue = null;
                    if (_type.IsValueType)
                    {
                        sourceValue = structValueGetter();
                    } else
                    {
                        sourceValue = valueGetter();
                    }
                     
                    if (sourceValue == null) continue;
                    var clone = CloneMakerFactory.GetCloneMaker(sourceValue, member.Item3, _typeMembers, _cloned).Clone(sourceValue, member.Item3);
                    if (_type.IsValueType)
                    {
                        object boxed = copy;
                        structValueSetter(boxed, clone);
                        copy = (T)boxed;
                        _cloned[source] = copy;
                    }
                    else
                    {
                        valueSetter(copy, clone);
                    }
                }
                return copy;
            }

            private T CreateInstance<T>(T source)
            {
                if (_type.IsValueType)
                {
                    return (T)Activator.CreateInstance(_type);
                }

                if (!_cachedConstrIL.TryGetValue(typeof(T), out Delegate myExec))
                {
                    DynamicMethod dymMethod = new DynamicMethod("DoClone", typeof(T), new Type[] { typeof(T) }, true);
                    ConstructorInfo cInfo = _type.GetConstructor(new Type[] { });

                    ILGenerator generator = dymMethod.GetILGenerator();

                    generator.Emit(OpCodes.Newobj, cInfo);

                    generator.Emit(OpCodes.Ret);

                    myExec = dymMethod.CreateDelegate(typeof(Func<T, T>));
                    _cachedConstrIL.Add(typeof(T), myExec);
                }
                return ((Func<T, T>)myExec)(source);
            }

            static Func<object, object> CreateGetter<T,TMemb>(FieldInfo field)
            {
                Type classType = field.DeclaringType;
                string fieldName = field.Name;

                if (GetGetterFromCache(classType, fieldName, out var getter)) return getter;

                ParameterExpression parameterExpression = Expression.Parameter(typeof(object));
                var cast = Expression.TypeAs(parameterExpression, field.DeclaringType);
                var expression = Expression.Field(cast, field);
                //Expression conversion = Expression.Convert(expression, typeof(object));
                getter = Expression.Lambda<Func<object, object>>(Expression.Convert(expression, typeof(object)), parameterExpression).Compile();

                AddGetterToCache(classType, fieldName, getter);

                return getter;
            }

            static Action<object, object> CreateSetter<T, TMemb>(FieldInfo field)
            {
                var type = field.FieldType;
                Type classType = field.DeclaringType;
                string fieldName = field.Name;

                if (GetSetterFromCache(classType, fieldName, out var setter)) return setter;

                ParameterExpression parameterExpression = Expression.Parameter(typeof(object));
                var cast = Expression.TypeAs(parameterExpression, field.DeclaringType);
                var expression = Expression.Field(cast, field);                
                var valueExp = Expression.Parameter(typeof(object));
                Expression conversion = Expression.Convert(valueExp, type);
                setter = Expression.Lambda<Action<object, object>>(Expression.Assign(expression, conversion), parameterExpression, valueExp).Compile();

                AddSetterToCache(classType, fieldName, setter);
                return setter;
            }

            static Func<object, object> CreatePropGetter<T, TMemb>(PropertyInfo property)
            {
                Type classType = property.DeclaringType;
                string propertyName = property.Name;

                if (GetGetterFromCache(classType, propertyName, out var getter)) return getter;

                ParameterExpression parameterExpression = Expression.Parameter(typeof(object));
                var cast = Expression.TypeAs(parameterExpression, property.DeclaringType);
                var expression = Expression.Property(cast, property);
                //Expression conversion = Expression.Convert(expression, typeof(object));
                getter = Expression.Lambda<Func<object, object>>(Expression.Convert(expression, typeof(object)), parameterExpression).Compile();

                AddGetterToCache(classType, propertyName, getter);
                return getter;
            }

            static Action<object, object> CreatePropSetter<T, TMemb>(PropertyInfo property)
            {
                var type = property.PropertyType;
                Type classType = property.DeclaringType;
                string propertyName = property.Name;

                if (GetSetterFromCache(classType, propertyName, out var setter)) return setter;

                ParameterExpression parameterExpression = Expression.Parameter(typeof(object));
                var cast = Expression.TypeAs(parameterExpression, property.DeclaringType);
                var expression = Expression.Property(cast, property);
                var valueExp = Expression.Parameter(typeof(object));
                Expression conversion = Expression.Convert(valueExp, type);
                setter = Expression.Lambda<Action<object, object>>(Expression.Assign(expression, conversion), parameterExpression, valueExp).Compile();

                AddSetterToCache(classType, propertyName, setter);
                return setter;
            }

            static bool GetGetterFromCache(Type classType, string name, out Func<object, object> getter)
            {
                if (dGets.TryGetValue(classType, out var typeDic))
                {
                    if (typeDic.TryGetValue(name, out var get))
                    {
                        getter = get;
                        return true;
                    }
                }
                getter = null;
                return false;
            }

            static bool GetSetterFromCache(Type classType, string name, out Action<object, object> setter)
            {
                if (dSets.TryGetValue(classType, out var typeDic))
                {
                    if (typeDic.TryGetValue(name, out var set))
                    {
                        setter = set;
                        return true;
                    }
                }
                setter = null;
                return false;
            }

            static void AddGetterToCache(Type classType, string name, Func<object, object> getter)
            {
                if (!dGets.ContainsKey(classType))
                {
                    var tempPropDict = new Dictionary<string, Func<object, object>>();
                    tempPropDict.Add(name, getter);

                    dGets.Add(classType, tempPropDict);
                }
                else
                {
                    if (!dGets[classType].ContainsKey(name))
                    {
                        dGets[classType].Add(name, getter);
                    }
                }
            }

            static void AddSetterToCache(Type classType, string name, Action<object, object> setter)
            {
                if (!dSets.ContainsKey(classType))
                {
                    var tempPropDict = new Dictionary<string, Action<object, object>>();
                    tempPropDict.Add(name, setter);

                    dSets.Add(classType, tempPropDict);
                }
                else
                {
                    if (!dSets[classType].ContainsKey(name))
                    {
                        dSets[classType].Add(name, setter);
                    }
                }
            }

            private static IEnumerable<Tuple<MemberInfo, MemberTypes, CloningMode>> GetCloneMode(IEnumerable<MemberInfo> members)
            {
                foreach (var member in members)
                {
                    var mode = GetCloneMode(member);
                    yield return new Tuple<MemberInfo, MemberTypes, CloningMode>(member, member.MemberType, mode);
                }
            }

            private static CloningMode GetCloneMode(MemberInfo member)
            {
                var mode = member.GetCustomAttribute<CloneableAttribute>(false);
                return mode?.Mode ?? CloningMode.Deep;
            }            
        }

        public class CollectionCloneMaker : ComplexCloneMaker
        {
            public CollectionCloneMaker(Type type, Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> typeMembers, Dictionary<Object, Object> cloned)
                : base(type, typeMembers, cloned)
            {
            }

            protected override T CloneComplex<T>(T source, CloningMode mode)
            {
                var addMethod = _type.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
                var collection = source as IEnumerable;
                var newCollection = (ICollection)Activator.CreateInstance(_type);
                _cloned.Add(source, newCollection);
                foreach (var item in collection)
                {
                    var value = CloneMakerFactory.GetCloneMaker(item, mode, _typeMembers, _cloned).Clone(item, mode);
                    addMethod.Invoke(newCollection, new object[] { value });
                }
                return (T)newCollection;
            }
        }

        public class ArrayCloneMaker : ComplexCloneMaker
        {
            public ArrayCloneMaker(Type type, Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> typeMembers, Dictionary<Object, Object> cloned)
                : base(type, typeMembers, cloned)
            {
            }

            protected override T CloneComplex<T>(T source, CloningMode mode)
            {
                var rank = _type.GetArrayRank();
                if (rank > 1)
                {
                    throw new ArgumentException("Only one-dimensional arrays are supported.", nameof(source));
                }
                var arr = source as Array;
                switch (mode)
                {
                    case CloningMode.Deep:
                        var elemType = _type.GetElementType();
                        var newArray = Array.CreateInstance(elemType, arr.Length);
                        _cloned.Add(source, newArray);
                        for (var i = 0; i < arr.Length; i++)
                        {
                            var value = CloneMakerFactory.GetCloneMaker(arr.GetValue(i), mode, _typeMembers, _cloned).Clone(arr.GetValue(i), mode);
                            newArray.SetValue(value, i);
                        }

                        return (T)(object)newArray;
                    case CloningMode.Shallow:
                        return (T)arr.Clone();
                    default:
                        throw new ArgumentException("Deep or shallow mode should be defined.", nameof(mode));
                }
            }
        }

        /// <summary>
        /// Return new copy for value types and reference copy for reference type.
        /// </summary>
        public class DefaultCloneMaker : CloneMaker
        {
            protected override T CloneInternal<T>(T source, CloningMode mode)
            {
                return source;
            }
        }

        public class StringCloneMaker : CloneMaker
        {
            protected override T CloneInternal<T>(T source, CloningMode mode)
            {
                if (source is string str)
                {
                    switch (mode)
                    {
                        case CloningMode.Deep:
                            return (T)(object)string.Copy(str);
                        case CloningMode.Shallow:
                            return source;
                        default:
                            throw new ArgumentException("Deep or shallow mode should be defined.", nameof(mode));
                    }
                }
                throw new ArgumentException("");
            }
        }

         



        public enum CloningMode
        {
            Deep = 0,
            Shallow = 1,
            Ignore = 2,
        }

        [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
        public sealed class CloneableAttribute : Attribute
        {
            public CloningMode Mode { get; }

            public CloneableAttribute(CloningMode mode)
            {
                Mode = mode;
            }
        }

        public class CloningServiceTest
        {
            public class Simple
            {
                public int I;
                public string S { get; set; }
                [Cloneable(CloningMode.Ignore)]
                public string Ignored { get; set; }
                [Cloneable(CloningMode.Shallow)]
                public object Shallow { get; set; }

                public virtual string Computed => S + I + Shallow;
            }

            public struct SimpleStruct
            {
                public int I;
                public string S { get; set; }
                [Cloneable(CloningMode.Ignore)]
                public string Ignored { get; set; }

                public string Computed => S + I;

                public SimpleStruct(int i, string s)
                {
                    I = i;
                    S = s;
                    Ignored = null;
                }
            }

            public class Simple2 : Simple
            {
                public double D;
                public SimpleStruct SS;
                public override string Computed => S + I + D + SS.Computed;
            }

            public class Node
            {
                public Node Left;
                public Node Right;
                public object Value;
                public int TotalNodeCount =>
                    1 + (Left?.TotalNodeCount ?? 0) + (Right?.TotalNodeCount ?? 0);
            }

            public ICloningService Cloner = new CloningService();
            public Action[] AllTests => new Action[] {
            SimpleTest,
            SimpleStructTest,
            Simple2Test,
            NodeTest,
            ArrayTest,
            CollectionTest,
            ArrayTest2,
            CollectionTest2,
            MixedCollectionTest,
            RecursionTest,
            RecursionTest2,
            PerformanceTest,
        };

            public static void Assert(bool criteria)
            {
                if (!criteria)
                    throw new InvalidOperationException("Assertion failed.");
            }

            public void Measure(string title, Action test)
            {
                //test(); // Warmup
                var sw = new Stopwatch();
                GC.Collect();
                sw.Start();
                test();
                sw.Stop();
                Console.WriteLine($"{title}: {sw.Elapsed.TotalMilliseconds:0.000}ms");
            }

            public void SimpleTest()
            {
                var s = new Simple() { I = 1, S = "2", Ignored = "3", Shallow = new object() };
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(s.Computed == c.Computed);
                Assert(c.Ignored == null);
                Assert(ReferenceEquals(s.Shallow, c.Shallow));
            }

            public void SimpleStructTest()
            {
                var s = new SimpleStruct(1, "2") { Ignored = "3" };
                var c = Cloner.Clone(s);
                Assert(s.Computed == c.Computed);
                Assert(c.Ignored == null);
            }

            public void Simple2Test()
            {
                var s = new Simple2()
                {
                    I = 1,
                    S = "2",
                    D = 3,
                    SS = new SimpleStruct(3, "4"),
                };
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(s.Computed == c.Computed);
            }

            public void NodeTest()
            {
                var s = new Node
                {
                    Left = new Node
                    {
                        Right = new Node()
                    },
                    Right = new Node()
                };
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(s.TotalNodeCount == c.TotalNodeCount);
            }

            public void RecursionTest()
            {
                var s = new Node();
                s.Left = s;
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(null == c.Right);
                Assert(c == c.Left);
            }

            public void ArrayTest()
            {
                var n = new Node
                {
                    Left = new Node
                    {
                        Right = new Node()
                    },
                    Right = new Node()
                };
                var s = new[] { n, n };
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(s.Sum(n1 => n1.TotalNodeCount) == c.Sum(n1 => n1.TotalNodeCount));
                Assert(c[0] == c[1]);
            }

            public void CollectionTest()
            {
                var n = new Node
                {
                    Left = new Node
                    {
                        Right = new Node()
                    },
                    Right = new Node()
                };
                var s = new List<Node>() { n, n };
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(s.Sum(n1 => n1.TotalNodeCount) == c.Sum(n1 => n1.TotalNodeCount));
                Assert(c[0] == c[1]);
            }

            public void ArrayTest2()
            {
                var s = new[] { new[] { 1, 2, 3 }, new[] { 4, 5 } };
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(15 == c.SelectMany(a => a).Sum());
            }

            public void CollectionTest2()
            {
                var s = new List<List<int>> { new List<int> { 1, 2, 3 }, new List<int> { 4, 5 } };
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(15 == c.SelectMany(a => a).Sum());
            }

            public void MixedCollectionTest()
            {
                var s = new List<IEnumerable<int[]>> {
                new List<int[]> {new [] {1}},
                new List<int[]> {new [] {2, 3}},
            };
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(6 == c.SelectMany(a => a.SelectMany(b => b)).Sum());
            }

            public void RecursionTest2()
            {
                var l = new List<Node>();
                var n = new Node { Value = l };
                n.Left = n;
                l.Add(n);
                var s = new object[] { null, l, n };
                s[0] = s;
                var c = Cloner.Clone(s);
                Assert(s != c);
                Assert(c[0] == c);
                var cl = (List<Node>)c[1];
                Assert(l != cl);
                var cn = cl[0];
                Assert(n != cn);
                Assert(cl == cn.Value);
                Assert(cn.Left == cn);
            }

            public void PerformanceTest()
            {
                Func<int, Node> makeTree = null;
                makeTree = depth => {
                    if (depth == 0)
                        return null;
                    return new Node
                    {
                        Value = depth,
                        Left = makeTree(depth - 1),
                        Right = makeTree(depth - 1),
                    };
                };
                for (var i = 10; i <= 20; i++)
                {
                    var root = makeTree(i);
                    Measure($"Cloning {root.TotalNodeCount} nodes", () => {
                        var copy = Cloner.Clone(root);
                        Assert(root != copy);
                    });
                }
            }

            public void RunAllTests()
            {
                foreach (var test in AllTests)
                    test.Invoke();
                Console.WriteLine("Done.");
            }
        }

        public class Solution
        {
            public static void Main(string[] args)
            {
                var cloningServiceTest = new CloningServiceTest();
                var allTests = cloningServiceTest.AllTests;
                while (true)
                {
                    var line = Console.ReadLine();
                    if (string.IsNullOrEmpty(line))
                        break;
                    var test = allTests[int.Parse(line)];
                    try
                    {
                        test.Invoke();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Failed on {test.GetMethodInfo().Name}.");
                    }
                }
                Console.WriteLine("Done.");
            }
        }
    }

}
