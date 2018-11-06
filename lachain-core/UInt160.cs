﻿using System;
using System.Collections;
using System.Linq;
using Phorkus.Core.Extension;

namespace Phorkus.Core
{
    public class UInt160 : IEquatable<UInt160>, IComparable<UInt160>
    {
        public const int BufferLength = 20;

        public static readonly UInt160 Zero = new UInt160();

        private readonly byte[] _buffer;

        public UInt160()
        {
            _buffer = new byte[Size];
        }

        public UInt160(byte[] value) : this()
        {
            if (value.Length != Size)
                throw new ArgumentException();

            Array.Copy(value, _buffer, _buffer.Length);
        }
        
        public static int Size => BufferLength;

        public bool Equals(UInt160 other)
        {
            if (other == null)
                return false;

            if (ReferenceEquals(this, other))
                return true;

            return _buffer.SequenceEqual(other._buffer);
        }

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (ReferenceEquals(this, obj))
                return true;

            if (obj is UInt160 other)
            {
                return _buffer.SequenceEqual(other._buffer);
            }

            return false;
        }

        public override int GetHashCode()
        {
            return _buffer.GetHashCode();
        }

        public int CompareTo(UInt160 other)
        {
            return ((IStructuralComparable)_buffer).CompareTo(other._buffer, StructuralComparisons.StructuralComparer);
        }

        public byte[] ToArray()
        {
            return _buffer.ToArray();
        }

        public override string ToString()
        {
            return _buffer.Reverse().ToHexString(true);
        }
        
        public string ToString(bool append0X)
        {
            return _buffer.Reverse().ToHexString(append0X);
        }
        
        public static UInt160 Parse(string value)
        {
            return value.IsHexString() ? new UInt160(value.HexToBytes(BufferLength * 2).Reverse().ToArray()) : value.ToScriptHash();
        }

        public static bool TryParse(string s, out UInt160 result)
        {
            try
            {
                result = Parse(s);
                return true;
            }
            catch
            {
                result = Zero;
                return false;
            }
        }

        public static bool operator ==(UInt160 left, UInt160 right)
        {
            return left?.Equals(right) ?? right is null;
        }

        public static bool operator !=(UInt160 left, UInt160 right)
        {
            return !(left?.Equals(right) ?? right is null);
        }

        public static bool operator >(UInt160 left, UInt160 right)
        {
            return left.CompareTo(right) > 0;
        }

        public static bool operator >=(UInt160 left, UInt160 right)
        {
            return left.CompareTo(right) >= 0;
        }

        public static bool operator <(UInt160 left, UInt160 right)
        {
            return left.CompareTo(right) < 0;
        }

        public static bool operator <=(UInt160 left, UInt160 right)
        {
            return left.CompareTo(right) <= 0;
        }
    }
}
