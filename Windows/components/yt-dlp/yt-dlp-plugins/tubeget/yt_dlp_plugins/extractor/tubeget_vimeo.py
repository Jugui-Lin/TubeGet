import re
import urllib.parse

from yt_dlp.extractor.vimeo import VimeoIE
from yt_dlp.utils import ExtractorError


class _TubeGetVimeoIE(VimeoIE, plugin_name='TubeGet'):
    def _real_extract(self, url):
        try:
            return super()._real_extract(url)
        except ExtractorError as error:
            # Vimeo's regular page extractor uses an authenticated API, while
            # some public videos are also available through their own player.
            parsed = urllib.parse.urlsplit(url)
            direct = re.fullmatch(r'/([0-9]+)/?', parsed.path)
            if (parsed.hostname not in ('vimeo.com', 'www.vimeo.com') or not direct
                    or 'client only works when logged-in' not in str(error)):
                raise
            player = urllib.parse.urlunsplit((
                'https', 'player.vimeo.com', f'/video/{direct.group(1)}', parsed.query, ''))
            return self.url_result(player, VimeoIE, direct.group(1))
