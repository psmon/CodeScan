---
name: 04-graph-gui
type: doc
anchor: none  # Prompt/ = 빌드 이력 프롬프트(설계-고아). 현재 코드 정책이 아니라 검사 면제.
---

CodeScan 은 CLI와 TUI 두가지 인터페이스를제공
- CLI : Command Line Interface
- TUI : Text User Interface

추가:
neo4j 와 같은 그래프 탐색을 추가로 적용
-닷넷에서 사용가능한 내장DB 라이브러리중 채택 (기존 내장DB와 동시업데이트)
- 검색기능을 CLI,TUI제공 - 일반키워드검색 + Graph 검색기능

- GUI : Graphical User Interface 신설 웹을 구동하여 뷰어를 제공.. start,stop 을제공 기본포트 8085 지정가능
- 키워드 검색기능 제공 
- Graph 검색기능 제공
- Graph의 경우 Graph 뷰를 통해 노드,엣지 정보를 제공
- Graph 뷰의 경우 2D인 경우 No4jClient View와 유사하게 뷰
- Graph 뷰 3d도 제공 소스노드 지식을 그래피컬하게 표시