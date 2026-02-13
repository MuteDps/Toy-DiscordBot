using System;
using System.Threading.Tasks;
using YoutubeTogether.MediaProcessor; // 네임스페이스 사용

class Program
{
    static async Task Main(string[] args)
    {
        var player = new VlcMediaController();

        // 1. 유튜브 플레이리스트 추가 (자동으로 펼쳐짐)
        // playlist?list=... 형태의 URL을 넣으면 됩니다.
        await player.AddAsync("https://www.youtube.com/playlist?list=PLw-VjHDlEOgvtnnnqWlTqByAtC7tXJQ6F");

        // 2. 단일 영상 추가
        await player.AddAsync("https://www.youtube.com/watch?v=dQw4w9WgXcQ");

        // 3. 목록 확인
        // 플레이리스트 내부의 곡들이 0번, 1번, 2번... 순서대로 쫙 나옵니다.
        var list = player.GetPlaylist();
        foreach (var item in list) Console.WriteLine(item);

        // 4. 조작
        player.Next();       // 다음 곡
        player.PlayPause();  // 일시정지
        player.RemoveAt(2);  // 2번 영상 삭제
        player.Clear();      // 전체 삭제

        Console.ReadLine(); // 종료 방지
    }
}