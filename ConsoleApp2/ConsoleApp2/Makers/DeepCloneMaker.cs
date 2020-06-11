using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;

namespace ConsoleApp2
{
    internal class DeepCloneMaker : ComplexCloneMaker
    {
        private static readonly Dictionary<Type, Dictionary<string, Func<object, object>>> dGets = new Dictionary<Type, Dictionary<string, Func<object, object>>>();

        private static readonly Dictionary<Type, Dictionary<string, Action<object, object>>> dSets = new Dictionary<Type, Dictionary<string, Action<object, object>>>();

        private static readonly Dictionary<Type, Delegate> _cachedConstrIL = new Dictionary<Type, Delegate>();

        public DeepCloneMaker(Type type, Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> typeMembers, Dictionary<object, object> cloned)
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
                }
                else
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

        static Func<object, object> CreateGetter<T, TMemb>(FieldInfo field)
        {
            Type classType = field.DeclaringType;
            string fieldName = field.Name;

            if (GetGetterFromCache(classType, fieldName, out var getter))
                return getter;

            ParameterExpression parameterExpression = Expression.Parameter(typeof(object));
            var cast = Expression.TypeAs(parameterExpression, field.DeclaringType);
            var expression = Expression.Field(cast, field);
            getter = CreateDelegate(Expression.Lambda<Func<object, object>>(Expression.Convert(expression, typeof(object)), parameterExpression), $"{classType}_{fieldName}");

            AddGetterToCache(classType, fieldName, getter);

            return getter;
        }

        static Action<object, object> CreateSetter<T, TMemb>(FieldInfo field)
        {
            var type = field.FieldType;
            Type classType = field.DeclaringType;
            string fieldName = field.Name;

            if (GetSetterFromCache(classType, fieldName, out var setter))
                return setter;

            ParameterExpression parameterExpression = Expression.Parameter(typeof(object));
            var cast = Expression.TypeAs(parameterExpression, field.DeclaringType);
            var expression = Expression.Field(cast, field);
            var valueExp = Expression.Parameter(typeof(object));
            Expression conversion = Expression.Convert(valueExp, type);
            setter = CreateDelegate(Expression.Lambda<Action<object, object>>(Expression.Assign(expression, conversion), parameterExpression, valueExp), $"{classType}_{fieldName}");

            AddSetterToCache(classType, fieldName, setter);
            return setter;
        }

        static Func<object, object> CreatePropGetter<T, TMemb>(PropertyInfo property)
        {
            Type classType = property.DeclaringType;
            string propertyName = property.Name;

            if (GetGetterFromCache(classType, propertyName, out var getter))
                return getter;

            ParameterExpression parameterExpression = Expression.Parameter(typeof(object));
            var cast = Expression.TypeAs(parameterExpression, property.DeclaringType);
            var expression = Expression.Property(cast, property);
            getter = CreateDelegate(Expression.Lambda<Func<object, object>>(Expression.Convert(expression, typeof(object)), parameterExpression), $"{classType}_{propertyName}");

            AddGetterToCache(classType, propertyName, getter);
            return getter;
        }

        static Action<object, object> CreatePropSetter<T, TMemb>(PropertyInfo property)
        {
            var type = property.PropertyType;
            Type classType = property.DeclaringType;
            string propertyName = property.Name;

            if (GetSetterFromCache(classType, propertyName, out var setter))
                return setter;

            ParameterExpression parameterExpression = Expression.Parameter(typeof(object));
            var cast = Expression.TypeAs(parameterExpression, property.DeclaringType);
            var expression = Expression.Property(cast, property);
            var valueExp = Expression.Parameter(typeof(object));
            Expression conversion = Expression.Convert(valueExp, type);
            setter = CreateDelegate(Expression.Lambda<Action<object, object>>(Expression.Assign(expression, conversion), parameterExpression, valueExp), $"{classType}_{propertyName}");

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

        static T CreateDelegate<T>(Expression<T> expression, string methodId) where T : Delegate
        {
            var ab = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName($"Assembly_{methodId}"), AssemblyBuilderAccess.Run);
            var mod = ab.DefineDynamicModule($"Module_{methodId}");
            var tb = mod.DefineType($"Type_{methodId}", TypeAttributes.Public);
            var mb = tb.DefineMethod(methodId, MethodAttributes.Public | MethodAttributes.Static);
            expression.CompileToMethod(mb);
            var t = tb.CreateType();
            return (T)Delegate.CreateDelegate(typeof(T), t.GetMethod(methodId));
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
}
