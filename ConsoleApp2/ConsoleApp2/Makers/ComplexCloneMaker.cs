using System;
using System.Collections.Generic;
using System.Reflection;

namespace ConsoleApp2
{
    internal abstract class ComplexCloneMaker : CloneMaker
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
}
