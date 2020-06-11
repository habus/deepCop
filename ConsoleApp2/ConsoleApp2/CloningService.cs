using System;
using System.Collections.Generic;
using System.Reflection;

namespace ConsoleApp2
{
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
}
