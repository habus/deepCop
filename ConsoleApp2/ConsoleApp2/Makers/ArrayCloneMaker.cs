using System;
using System.Collections.Generic;
using System.Reflection;

namespace ConsoleApp2
{
    internal class ArrayCloneMaker : ComplexCloneMaker
    {
        public ArrayCloneMaker(Type type, Dictionary<Type, IReadOnlyCollection<Tuple<MemberInfo, MemberTypes, CloningMode>>> typeMembers, Dictionary<object, object> cloned)
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
}
