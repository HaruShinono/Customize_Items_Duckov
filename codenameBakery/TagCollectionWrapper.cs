using System;
using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace codenameBakery
{
    /// <summary>
    /// Một lớp bao bọc (wrapper) cung cấp giao diện IList cho một collection cơ bản bằng cách sử dụng reflection.
    /// Điều này cho phép tương tác với các collection dưới dạng danh sách tiêu chuẩn ngay cả khi chúng không triển khai IList.
    /// </summary>
    public class TagCollectionWrapper : IList, ICollection, IEnumerable
    {
        private readonly object _collectionInstance;
        private readonly MethodInfo _addMethod;
        private readonly PropertyInfo _countProperty;

        /// <summary>
        /// Khởi tạo một thể hiện mới của TagCollectionWrapper.
        /// </summary>
        /// <param name="collectionInstance">Đối tượng collection thực tế cần bao bọc.</param>
        /// <param name="addMethod">MethodInfo cho phương thức 'Add' của collection.</param>
        /// <param name="countProperty">PropertyInfo cho thuộc tính 'Count' của collection.</param>
        public TagCollectionWrapper(object collectionInstance, MethodInfo addMethod, PropertyInfo countProperty)
        {
            _collectionInstance = collectionInstance;
            _addMethod = addMethod;
            _countProperty = countProperty;
        }

        /// <summary>
        /// Lấy số lượng phần tử trong collection.
        /// </summary>
        public int Count
        {
            get
            {
                try
                {
                    // Sử dụng reflection để lấy giá trị của thuộc tính Count đã được cung cấp.
                    return (int)_countProperty.GetValue(_collectionInstance);
                }
                catch
                {
                    return 0;
                }
            }
        }

        public bool IsReadOnly => false;
        public bool IsFixedSize => false;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        /// <summary>
        /// Lấy hoặc đặt phần tử tại một chỉ mục cụ thể. Việc đặt giá trị không được hỗ trợ.
        /// </summary>
        public object this[int index]
        {
            get
            {
                try
                {
                    // Thử truy cập bằng indexer 'Item' nếu có (hiệu quả hơn).
                    PropertyInfo indexerProperty = _collectionInstance.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public);
                    if (indexerProperty != null)
                    {
                        return indexerProperty.GetValue(_collectionInstance, new object[] { index });
                    }

                    // Nếu không, duyệt qua collection để tìm phần tử.
                    IEnumerator enumerator = GetEnumerator();
                    for (int i = 0; i <= index; i++)
                    {
                        if (!enumerator.MoveNext())
                        {
                            return null; // Chỉ mục nằm ngoài phạm vi.
                        }
                    }
                    return enumerator.Current;
                }
                catch
                {
                    return null;
                }
            }
            set
            {
                throw new NotSupportedException("The collection is read-only or does not support setting items by index.");
            }
        }

        /// <summary>
        /// Thêm một phần tử vào collection.
        /// </summary>
        /// <returns>Chỉ mục của phần tử mới được thêm vào.</returns>
        public int Add(object value)
        {
            try
            {
                _addMethod.Invoke(_collectionInstance, new object[] { value });
                return Count - 1;
            }
            catch
            {
                return -1;
            }
        }

        /// <summary>
        /// Xóa tất cả các phần tử khỏi collection.
        /// </summary>
        public void Clear()
        {
            try
            {
                MethodInfo clearMethod = _collectionInstance.GetType().GetMethod("Clear", BindingFlags.Instance | BindingFlags.Public);
                clearMethod?.Invoke(_collectionInstance, null);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error trying to clear the wrapped collection: {ex.Message}");
            }
        }

        /// <summary>
        /// Kiểm tra xem collection có chứa một giá trị cụ thể hay không.
        /// </summary>
        public bool Contains(object value)
        {
            foreach (object item in this)
            {
                if (item != null && item.Equals(value))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Tìm chỉ mục của một giá trị cụ thể trong collection.
        /// </summary>
        public int IndexOf(object value)
        {
            int index = 0;
            foreach (object item in this)
            {
                if (item != null && item.Equals(value))
                {
                    return index;
                }
                index++;
            }
            return -1;
        }

        public void Insert(int index, object value)
        {
            throw new NotSupportedException("Inserting an element at a specific index is not supported.");
        }

        /// <summary>
        /// Xóa một giá trị cụ thể khỏi collection.
        /// </summary>
        public void Remove(object value)
        {
            try
            {
                MethodInfo removeMethod = _collectionInstance.GetType().GetMethod("Remove", BindingFlags.Instance | BindingFlags.Public);
                removeMethod?.Invoke(_collectionInstance, new object[] { value });
            }
            catch (Exception ex)
            {
                 Debug.LogError($"Error trying to remove an item from the wrapped collection: {ex.Message}");
            }
        }

        public void RemoveAt(int index)
        {
            throw new NotSupportedException("Removing an element at a specific index is not supported.");
        }

        /// <summary>
        /// Sao chép các phần tử của collection vào một mảng.
        /// </summary>
        public void CopyTo(Array array, int index)
        {
            int currentIndex = index;
            foreach (object item in this)
            {
                array.SetValue(item, currentIndex++);
            }
        }

        /// <summary>
        /// Trả về một enumerator để duyệt qua collection.
        /// </summary>
        public IEnumerator GetEnumerator()
        {
            try
            {
                MethodInfo getEnumeratorMethod = _collectionInstance.GetType().GetMethod("GetEnumerator", BindingFlags.Instance | BindingFlags.Public);
                if (getEnumeratorMethod != null)
                {
                    IEnumerator enumerator = getEnumeratorMethod.Invoke(_collectionInstance, null) as IEnumerator;
                    if (enumerator != null)
                    {
                        return enumerator;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error getting enumerator for wrapped collection: {ex.Message}");
            }
            // Trả về một enumerator rỗng như một giải pháp an toàn.
            return new ArrayList().GetEnumerator();
        }
    }
}