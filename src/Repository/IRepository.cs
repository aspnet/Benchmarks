using System.Collections.Generic;

namespace Repository
{
    public interface IRepository<T> where T: IIdentifiable
    {
        T Add(T item);
        IEnumerable<T> GetAll();
        T Find(int id);
        T Remove(int id);
        void Update(T item);

    }
}
