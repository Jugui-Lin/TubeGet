import re
import urllib.parse

from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.extractor.vimeo import VimeoIE
from yt_dlp.extractor.youtube import YoutubeIE
from yt_dlp.utils import ExtractorError, extract_attributes, smuggle_url, urljoin


class TubeGetBehanceIE(InfoExtractor):
    IE_NAME = 'behance'
    _VALID_URL = r'https?://(?:www\.)?behance\.net/gallery/(?P<id>\d+)/[^/?#]+'

    def _real_extract(self, url):
        project_id = self._match_id(url)
        # Use the same public project path without feed-tracking parameters.
        path = urllib.parse.urlsplit(url).path.rstrip('/')
        page_url = f'https://www.behance.net{path}'
        webpage, response = self._download_webpage_handle(
            page_url, project_id, expected_status=403)
        if response.status == 403:
            challenge = self._search_regex(
                r'document\.cookie\s*=\s*["\x27]js_challenge_value=([a-fA-F0-9]{32,512});\s*Path=/["\x27]',
                webpage, 'Behance visitor cookie', default=None)
            if not challenge or urllib.parse.urlsplit(response.url).hostname != 'www.behance.net':
                raise ExtractorError('Behance 拒绝访问此作品。请先在浏览器中确认能打开该作品。', expected=True)
            self._set_cookie('www.behance.net', 'js_challenge_value', challenge, secure=True)
            webpage = self._download_webpage(page_url, project_id, 'Loading Behance project')

        title = self._og_search_title(webpage, default=None) or self._html_extract_title(webpage, default=project_id)
        title = re.sub(r'\s*::\s*Behance\s*$', '', title)
        entries = []
        seen = set()
        for tag in re.findall(r'<iframe\b[^>]*>', webpage, flags=re.IGNORECASE):
            attributes = extract_attributes(tag)
            embed = urljoin(page_url, attributes.get('src') or attributes.get('data-src'))
            if not embed or embed in seen:
                continue
            if VimeoIE.suitable(embed):
                provider = VimeoIE
                target = smuggle_url(embed, {'referer': page_url})
            elif YoutubeIE.suitable(embed):
                provider = YoutubeIE
                target = embed
            else:
                continue
            seen.add(embed)
            entries.append(self.url_result(target, provider, video_title=f'{title} · {len(entries) + 1}'))

        if not entries:
            raise ExtractorError('作品页中未找到可解析的 Vimeo 或 YouTube 视频。', expected=True)
        if len(entries) == 1:
            return entries[0]
        if not self._yes_playlist(project_id, '1'):
            return entries[0]
        return self.playlist_result(entries, project_id, title)
