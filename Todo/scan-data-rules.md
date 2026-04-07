# 스캔 데이터 관리 규칙

## 현재 동작 (v0.3.x)

스캔 실행 시 기존 데이터를 삭제하지 않고 **누적(append)** 방식으로 동작한다.

### 테이블별 동작

| 테이블 | 스캔 시 동작 | 비고 |
|--------|-------------|------|
| `projects` | UPSERT (같은 경로면 기존 row 재사용) | `addinfo`는 스캔과 무관하게 유지 |
| `scans` | 매번 새 row INSERT | 스캔 이력이 계속 누적됨 |
| `files` | 새 scan_id로 INSERT | 이전 스캔의 파일 데이터도 남아있음 |
| `methods` | 새 scan_id의 file_id로 INSERT | 이전 스캔의 메서드 데이터도 남아있음 |
| `comments` | 새 scan_id의 file_id로 INSERT | 이전 스캔의 코멘트 데이터도 남아있음 |
| `project_docs` | 새 scan_id로 INSERT | README/AGENT.md 등 |
| `search_index` | 새 scan_id로 INSERT | FTS5 인덱스, 이전 데이터도 검색 대상 |

### 조회 규칙

- `project` 상세: `MAX(scan_id)`로 **최신 스캔만** 표시
- `search`: **전체 스캔 데이터** 대상으로 검색 (scan_id 필터 없음)
- `projects` 목록: projects 테이블의 stats (마지막 스캔 기준 갱신됨)

### project-update --source 동작

1. git pull (실패 시 경고 후 계속)
2. 전체 스캔 (directory + method/comment + git blame)
3. 새 scan 레코드로 DB INSERT (기존 데이터 유지)

---

## 개선 아젠다

### 1. 검색 중복 결과 문제
- **현상**: `--source`를 반복하면 search_index에 동일 데이터가 누적되어 검색 시 중복 결과 발생
- **방안 A**: 소스 업데이트 시 해당 프로젝트의 이전 search_index 엔트리 삭제 후 재생성
- **방안 B**: 검색 시 최신 scan_id만 필터링 (scan_id IN (SELECT MAX(id) FROM scans ...))

### 2. 스캔 이력 정리 (retention)
- **현상**: 스캔을 반복하면 files/methods/comments가 무한 누적되어 DB 비대화
- **방안 A**: 최신 N개 스캔만 유지하고 오래된 스캔 cascade 삭제
- **방안 B**: `project-update --cleanup` 옵션으로 수동 정리
- **방안 C**: 자동 정리 - 일정 개수 초과 시 오래된 것부터 삭제

### 3. 증분 스캔 (incremental scan)
- **현상**: `--source`는 항상 full scan으로 파일이 많으면 시간 소요
- **방안**: git diff로 변경된 파일만 재분석하여 기존 스캔에 merge
