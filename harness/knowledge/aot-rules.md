---
name: aot-rules
type: knowledge
domain: build
description: .NET Native AOT 단일 바이너리 배포를 위한 규칙과 위험 API 목록.
---

# AOT 규칙 (CodeScan)

## 배포 모델

`Script/deploy-win.ps1` / `Script/deploy-linux.sh`는 `dotnet publish -c Release`로 단일 바이너리를 빌드해 `~/.codescan/bin`에 설치한다.
AOT 환경에서는 **런타임에 코드 생성/리플렉션이 불가**하다.

## 금지/주의 API

| API | 위험 | 대안 |
|-----|------|------|
| `new Regex(...)` | 트리밍/AOT | `[GeneratedRegex]` |
| `JsonSerializer.Serialize<T>(obj)` | 리플렉션 | `JsonSerializerContext` 소스 제너레이터 |
| `Activator.CreateInstance` | 리플렉션 | 컴파일 타임 팩토리 |
| `typeof(T).GetMethod(...).Invoke` | 리플렉션 | 인터페이스/대리자 |
| `Assembly.Load(...)` | 동적 로딩 | 정적 참조 |
| `Type.GetType("...")` 문자열 인자 | 트리밍에 잘림 | `typeof(T)` 직접 사용 |
| `Expression.Compile()` | 런타임 IL | 사전 컴파일 |

## IL 경고 코드

| 코드 | 의미 |
|------|------|
| IL2026 | RequiresUnreferencedCode (트리밍 위험) |
| IL2070/IL2075 | DynamicallyAccessedMembers 위반 |
| IL3050 | AOT 미지원 API 호출 |
| IL3053 | 어셈블리에 RequiresDynamicCode |

**모든 IL2xxx/IL3xxx 경고는 무시 금지** — 어노테이션하거나 코드 변경.

## csproj 권장 설정

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <InvariantGlobalization>true</InvariantGlobalization>
  <TrimMode>full</TrimMode>
  <IsTrimmable>true</IsTrimmable>
</PropertyGroup>
```

## 검증 체크리스트

- [ ] `dotnet publish -c Release` 에러 0건
- [ ] IL/AOT 경고 Critical/High 0건
- [ ] 생성된 단일 바이너리 실행 → 핵심 명령(`scan`, `search`, `tui`) 동작
- [ ] SQLite 네이티브 라이브러리 동봉 확인
- [ ] 새 의존성 NuGet 추가 시 AOT 지원 여부 확인 (`IsAotCompatible` 또는 패키지 문서)

## 참고

- SQLite/FTS5는 네이티브 인터롭 — RID(`win-x64`, `linux-x64`) 별 빌드 확인 필수.
- 신규 의존성 도입 전 정찰병에게 사전 점검 요청 권장.
