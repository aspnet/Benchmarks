namespace Rewrite.Data
{
    public interface IDb
    {
        Task<Fortune> LoadSingleQueryRow();
    }
}
