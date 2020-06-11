using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ConsoleApp2
{
    internal class CollectionCloneMaker : ComplexCloneMaker
    {
        private const string AddMethod = "Add";
        
        public CollectionCloneMaker(Type type, Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> typeMembers, Dictionary<object, object> cloned)
            : base(type, typeMembers, cloned)
        {
        }

        protected override T CloneComplex<T>(T source, CloningMode mode)
        {
            var addMethod = _type.GetMethod(AddMethod, BindingFlags.Instance | BindingFlags.Public);
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
}
