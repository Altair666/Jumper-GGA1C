using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace GGA1C.Sync.Templates;

/// <summary>
/// Хранилище файлов шаблонов. Хаб знает только непрозрачную ссылку: сейчас это имя файла в
/// папке (<see cref="FolderTemplateFileStore"/>), потом — идентификатор в FastAPI-сервисе
/// файлов на NAS (карточка «ФАЙЛ»). Поэтому договор асинхронный и потоковый, без путей.
/// </summary>
public interface ITemplateFileStore
{
    /// <summary>Сохранить содержимое; <paramref name="fileName"/> — для расширения и подсказки. Возвращает ссылку.</summary>
    Task<string> SaveAsync(Stream content, string fileName, CancellationToken cancellationToken = default);

    /// <summary>Открыть файл по ссылке на чтение. Нет файла — <see cref="FileNotFoundException"/>.</summary>
    Task<Stream> OpenAsync(string fileRef, CancellationToken cancellationToken = default);

    /// <summary>Удалить файл; нет файла — не ошибка.</summary>
    Task DeleteAsync(string fileRef, CancellationToken cancellationToken = default);
}
