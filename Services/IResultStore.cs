namespace CodeScan.Services;

/// <summary>
/// 탐색/검색 결과 저장 인터페이스.
/// 현재: FileResultStore (파일 로깅)
/// 추후: DB 구현체로 교체 가능
/// </summary>
public interface IResultStore
{
    void Save(string command, string content);
}
