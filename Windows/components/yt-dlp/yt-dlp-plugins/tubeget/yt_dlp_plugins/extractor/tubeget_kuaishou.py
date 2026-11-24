import urllib.parse

from yt_dlp.extractor.common import InfoExtractor
from yt_dlp.utils import int_or_none, traverse_obj, url_or_none


class TubeGetKuaishouIE(InfoExtractor):
    IE_NAME = 'kuaishou'
    _VALID_URL = r'''(?x)https?://(?:
        v\.kuaishou\.com/(?P<share>[A-Za-z0-9]+)|
        (?:www\.)?kuaishou\.com/short-video/(?P<desktop>[A-Za-z0-9]+)|
        (?:[a-z0-9-]+\.)?m\.(?:gifshow|chenzhongtech)\.com/fw/photo/(?P<mobile>[A-Za-z0-9]+)
    )(?:[/?#]|$)'''

    def _real_extract(self, url):
        share_id, desktop_id, mobile_id = self._match_valid_url(url).group('share', 'desktop', 'mobile')
        video_id = share_id or desktop_id or mobile_id
        if desktop_id:
            url = f'https://m.gifshow.com/fw/photo/{desktop_id}'

        # The public share page serves its video data to mobile visitors.
        webpage, response = self._download_webpage_handle(url, video_id, headers={
            'User-Agent': 'Mozilla/5.0 (Linux; Android 13) AppleWebKit/537.36 '
                          '(KHTML, like Gecko) Chrome/131.0.0.0 Mobile Safari/537.36',
        })
        target = self._match_valid_url(response.url)
        if not target:
            self.raise_no_formats('快手分享链接没有跳转到可识别的作品页。', expected=True)
        video_id = target.group('mobile') or target.group('desktop') or video_id

        state = self._search_json(r'window\.INIT_STATE\s*=', webpage, 'video data', video_id, default={})
        photo = traverse_obj(state, (..., 'photo', {dict}), get_all=False) or {}
        formats = []
        for stream in traverse_obj(photo, ('manifest', 'adaptationSet', ..., 'representation', ..., {dict})):
            media_url = url_or_none(stream.get('url'))
            if not media_url or stream.get('hidden') or stream.get('mute'):
                continue
            formats.append({
                'url': media_url,
                'format_id': str(stream.get('id') or len(formats) + 1),
                'ext': 'mp4',
                'width': int_or_none(stream.get('width')),
                'height': int_or_none(stream.get('height')),
                'fps': int_or_none(stream.get('frameRate')),
                'tbr': int_or_none(stream.get('avgBitrate')),
                'filesize': int_or_none(stream.get('fileSize')),
                'vcodec': {'avc': 'h264', 'hevc': 'hevc'}.get(stream.get('videoCodec')),
            })
        media_url = traverse_obj(photo, ('mainMvUrls', ..., 'url', {url_or_none}), get_all=False)
        if media_url:
            formats.append({
                'url': media_url,
                'format_id': 'public',
                'ext': 'mp4',
                'width': int_or_none(photo.get('width')),
                'height': int_or_none(photo.get('height')),
                'preference': -1,
            })
        self._remove_duplicate_formats(formats)
        if not formats:
            self.raise_no_formats('快手页面未提供可播放视频。请确认分享链接仍能在浏览器中播放。', expected=True)

        caption = photo.get('caption')
        return {
            'id': video_id,
            'title': caption or self._og_search_title(webpage, default=video_id),
            'uploader': photo.get('userName'),
            'duration': int_or_none(photo.get('duration'), scale=1000),
            'timestamp': int_or_none(photo.get('timestamp'), scale=1000),
            'thumbnail': traverse_obj(photo, ('coverUrls', ..., 'url', {url_or_none}), get_all=False),
            'formats': formats,
            'http_headers': {'Referer': urllib.parse.urljoin(response.url, '/')},
        }
