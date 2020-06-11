using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ConsoleApp2
{
    internal class CloneMakerFactory
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
}
