﻿using System;
using System.IO;
using TypeSupport.Extensions;
using static AnySerializer.TypeManagement;

namespace AnySerializer
{
    /// <summary>
    /// Validates serialization result data
    /// </summary>
    public class ValidatorInternal
    {
        /// <summary>
        /// Validate a byte array for valid serialization data
        /// </summary>
        /// <param name="bytes"></param>
        /// <returns></returns>
        public bool Validate(byte[] bytes)
        {
            var isValid = false;
            try
            {
                TypeDescriptors typeDescriptors = null;
                using (var stream = new MemoryStream(bytes))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        /**
                         * [SettingsByte]           1 byte (SerializerDataSettings)
                         * 
                         * Chunk Format 
                         * [ChunkType]              1 byte (byte)
                         * [ChunkLength]            4 bytes (Int32)
                         * [ObjectReferenceId]      2 bytes (UInt16)
                         * [OptionalTypeDescriptor] 2 bytes (UInt16)
                         * [Data]           [ChunkLength-Int32] bytes
                         * 
                         * Chunks may contain value types or Chunks, it's a recursive structure.
                         * By reading if you've read all of the data bytes, you know you've read 
                         * the whole structure.
                         */
                        // read in byte 0, the data settings
                        var dataSettings = (SerializerDataSettings)reader.ReadByte();
                        // read in all chunks
                        isValid = ReadChunk(reader, typeDescriptors, dataSettings);
                    }
                }
            }
            catch (Exception ex)
            {
                isValid = false;
            }

            return isValid;
        }

        /// <summary>
        /// Read a single chunk of data, recursively.
        /// </summary>
        /// <param name="reader"></param>
        /// <returns></returns>
        private bool ReadChunk(BinaryReader reader, TypeDescriptors typeDescriptors, SerializerDataSettings dataSettings)
        {
            var isChunkValid = true;
            var objectTypeIdByte = reader.ReadByte();
            var objectTypeId = (TypeId)objectTypeIdByte;
            var isNullValue = TypeUtil.IsNullValue(objectTypeId);
            var isTypeMapped = TypeUtil.IsTypeMapped(objectTypeId);
            var isTypeDescriptorMap = TypeUtil.IsTypeDescriptorMap(objectTypeId);

            // strip the flags and get only the type
            var objectTypeIdOnly = TypeUtil.GetTypeId(objectTypeIdByte);
            var isValueType = TypeUtil.IsValueType(objectTypeIdOnly);

            uint length = 0;
            if(dataSettings.BitwiseHasFlag(SerializerDataSettings.Compact))
                length = reader.ReadUInt16();
            else
                length = reader.ReadUInt32();
            var lengthStartPosition = reader.BaseStream.Position;
            ushort objectReferenceId = 0;
            ushort typeDescriptorId = 0;


            if (isTypeDescriptorMap)
            {
                uint dataLength = 0;
                if (dataSettings.BitwiseHasFlag(SerializerDataSettings.Compact))
                    dataLength = length - sizeof(ushort);
                else
                    dataLength = length - sizeof(uint);
                // read in the type descriptor map
                typeDescriptors = TypeReader.GetTypeDescriptorMap(reader, dataLength);
                // continue reading the data
                return ReadChunk(reader, typeDescriptors, dataSettings);
            }
            else
            {
                // read the object reference id
                objectReferenceId = reader.ReadUInt16();
            }

            // only interfaces can store type descriptors
            if (typeDescriptors != null && isTypeMapped)
            {
                typeDescriptorId = reader.ReadUInt16();
                if (!typeDescriptors.Contains(typeDescriptorId))
                    return false;
            }

            var objectTypeSupport = TypeUtil.GetType(objectTypeIdOnly);

            // value type
            if (length > 0)
            {
                switch (objectTypeId)
                {
                    // these types may contain additional chunks, only value types may not.
                    case TypeId.Array:
                    case TypeId.Tuple:
                    case TypeId.IDictionary:
                    case TypeId.IEnumerable:
                    case TypeId.Object:
                        isChunkValid = ReadChunk(reader, typeDescriptors, dataSettings);
                        break;
                }
                if (!isChunkValid)
                    return false;

                if(reader.BaseStream.Position - lengthStartPosition - sizeof(ushort) == 0)
                {
                    // it's a value type, read the full data
                    var data = reader.ReadBytes((int)length - Constants.LengthHeaderSize);
                }
                else if(reader.BaseStream.Position < reader.BaseStream.Length)
                {
                    // read another chunk
                    isChunkValid = ReadChunk(reader, typeDescriptors, dataSettings);
                    if (!isChunkValid)
                        return false;
                }
            }
            return isChunkValid;
        }

    }
}
