using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using YoutubeTogether;

/*
 *   !play <YouTube URL> — 단일 비디오 또는 플레이리스트를 추가합니다.
    플레이리스트인 경우: 첫 항목은 가능한 한 빨리 재생되도록 바로 처리하고, 나머지 항목은 백그라운드 작업으로 순차적으로 enqueue합니다.
    플레이리스트의 항목 수가 많으면 처리에 시간이 걸릴 수 있습니다. !jobs로 진행 상태를 확인하세요.
    !skip — 현재 재생 건 건너뛰기
    !queue — 플레이리스트 보기
       - 디스코드의 2천자 제한으로 인해, 페이지네이션 필수, 한 번에 10개 항목씩 표시 / 페이지 갯수 계산하여,
            페이지 번호는 1부터 시작, !queue <페이지 번호>로 페이지 이동 , 페이지 1번에는 현재 재생중인 영상도 함께 출력
    !remove <번호> — !queue에서 표시된 번호로 항목 삭제
    !clear — 전체 대기열 삭제
    !stop — 재생 정지
    !jobs — 현재 실행 중인 백그라운드 작업 목록(요청자 포함)
    !stats — 완료된 작업 통계(설명+요청자별 평균/최대 시간)
    !help — 도움말(플레이리스트 동기화 주의 포함)
 */
namespace YoutubeTogether.MediaProcessor
{
    public class MediaController
    {
        private static System.Lazy<MediaController> _instance = new System.Lazy<MediaController>(() => new MediaController());
        public static MediaController Instance => _instance.Value;

    }
}
