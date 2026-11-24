# Adapted from yt-dlp PR #17826 (Unlicense), by viking1304 and contributors.
# https://github.com/yt-dlp/yt-dlp/pull/17826
import hashlib
import json
import time
import urllib.parse
import uuid

from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.extractor.tiktok import DouyinIE
from yt_dlp.utils import ExtractorError, traverse_obj


class TubeGetDouyinShareIE(InfoExtractor):
    _VALID_URL = r'''(?x)https?://(?:
        (?:www\.)?douyin\.com/(?!video/)[^?#]*\?(?:[^#&]*&)*?modal_id=
        |(?:www\.)?iesdouyin\.com/share/video/
    )(?P<id>[0-9]+)(?=[/?&#]|$)'''

    def _real_extract(self, url):
        video_id = self._match_id(url)
        return self.url_result(f'https://www.douyin.com/video/{video_id}', DouyinIE, video_id)


class _TubeGetDouyinIE(DouyinIE, plugin_name="TubeGet"):
    _DETAIL_URL = 'https://www.douyin.com/aweme/v1/web/aweme/detail/'
    _AID = 6383
    _SEC_SALT = 'A96D855A08C0A9707F8BEF0D9A527E4E'
    _VISITOR_COOKIES = ('UIFID', 'UIFID_TEMP', 'UIFIDTEMP', 'uifid', 'uifid_temp', 'uifidtemp')

    def _visitor_id(self):
        return traverse_obj(
            self._get_cookies(self._WEBPAGE_HOST),
            (self._VISITOR_COOKIES, 'value', {str}), get_all=False)

    def _real_extract(self, url):
        video_id = self._match_id(url)
        if not self._get_cookies(self._WEBPAGE_HOST).get('ttwid'):
            self._request_webpage(
                f'{self._WEBPAGE_HOST}ttwid/register', video_id, 'Opening a Douyin visitor session',
                data=json.dumps({'aid': self._AID, 'service': 'www.douyin.com'}).encode(),
                headers={'Content-Type': 'application/json'}, fatal=False)

        if not self._get_cookies(self._WEBPAGE_HOST).get('passport_csrf_token'):
            self._set_cookie('.douyin.com', 'passport_csrf_token', uuid.uuid4().hex)

        visitor_id = self._visitor_id()
        if not visitor_id:
            response = self._request_webpage(
                self._WEBPAGE_HOST, video_id, 'Getting Douyin visitor information', fatal=False)
            visitor_id = self._visitor_id() or (response and response.headers.get('x-tt-trace-host'))
        if not visitor_id:
            raise ExtractorError('无法建立抖音访客会话。请在「登录选项」获取抖音 Cookies 后重试。', expected=True)

        timestamp = str(int(time.time()))
        query = {'aweme_id': video_id, 'aid': self._AID, 'uifid': visitor_id, 'timestamp': timestamp}
        encoded = urllib.parse.urlencode(query)
        signature = hashlib.md5(f'{visitor_id}_{timestamp}_{self._SEC_SALT}_{encoded}'.encode()).hexdigest()
        detail = traverse_obj(self._download_json(
            self._DETAIL_URL, video_id, 'Downloading Douyin video details',
            query={**query, 'x-secsdk-web-signature': signature},
            headers={'Referer': self._WEBPAGE_HOST}, fatal=False), ('aweme_detail', {dict}))
        if not detail:
            raise ExtractorError('抖音未返回该作品的播放信息。请确认作品可以打开，或在「登录选项」获取抖音 Cookies 后重试。', expected=True)

        return {**self._parse_aweme_video_app(detail), 'http_headers': {'Referer': self._WEBPAGE_HOST}}
