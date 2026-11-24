import re

from yt_dlp.extractor.bilibili import BiliBiliIE
from yt_dlp.utils import int_or_none, parse_qs, traverse_obj, url_or_none


class _TubeGetBilibiliPartsIE(BiliBiliIE, plugin_name='TubeGet'):
    def _real_extract(self, url):
        result = super()._real_extract(url)
        video_id = result.get('id')
        if (result.get('_type') != 'playlist'
                or not re.fullmatch(r'BV[A-Za-z0-9]+', video_id or '')
                or not re.match(r'https?://[^/]+/video/', url)):
            return result

        view = self._download_json(
            'https://api.bilibili.com/x/web-interface/view', video_id,
            'Reading anthology covers', query={'bvid': video_id}, fatal=False,
            headers={'Referer': 'https://www.bilibili.com/'})
        data = traverse_obj(view, ('data', {dict})) or {}
        if not data:
            return result

        cover = url_or_none(data.get('pic'))
        if cover and not result.get('thumbnail'):
            result['thumbnail'] = self._proto_relative_url(cover, 'https:').replace('http://', 'https://', 1)
        uploader = traverse_obj(data, ('owner', 'name', {str}))
        if uploader and not result.get('uploader'):
            result['uploader'] = uploader
        parts = {
            page['page']: page for page in traverse_obj(data, ('pages', ..., {dict}))
            if isinstance(page.get('page'), int)
        }

        def enrich(entries):
            for entry in entries:
                index = int_or_none(traverse_obj(parse_qs(entry.get('url') or ''), ('p', 0)))
                part = parts.get(index, {})
                thumbnail = url_or_none(part.get('first_frame')) or cover
                if thumbnail and not entry.get('thumbnail'):
                    entry['thumbnail'] = self._proto_relative_url(thumbnail, 'https:').replace('http://', 'https://', 1)
                if part.get('part') and not entry.get('title'):
                    entry['title'] = part['part']
                if part.get('duration') is not None and entry.get('duration') is None:
                    entry['duration'] = part['duration']
                yield entry

        result['entries'] = enrich(result['entries'])
        return result
