from urllib.parse import urlsplit

from yt_dlp.extractor.xinpianchang import XinpianchangIE
from yt_dlp.networking.exceptions import HTTPError
from yt_dlp.utils import ExtractorError


class _TubeGetXinpianchangIE(XinpianchangIE, plugin_name="TubeGet"):
    def _real_extract(self, url):
        info = super()._real_extract(url)
        if not self.get_param("http_headers", {}).get("Referer"):
            referer = f"https://www.xinpianchang.com/{self._match_id(url)}"
            for media_format in info["formats"]:
                host = urlsplit(media_format.get("url") or "").hostname or ""
                if host == "xpccdn.com" or host.endswith(".xpccdn.com"):
                    # The 4K CDN requires the article Referer as well as Range.
                    media_format.setdefault("http_headers", {}).setdefault("Referer", referer)
        return info

    def _download_webpage(self, url_or_request, video_id, *args, **kwargs):
        try:
            return super()._download_webpage(url_or_request, video_id, *args, **kwargs)
        except ExtractorError as error:
            if (
                not isinstance(url_or_request, str)
                or not self.suitable(url_or_request)
                or not isinstance(error.cause, HTTPError)
                or error.cause.status not in (403, 444)
            ):
                raise
            status = error.cause.status

        # The mobile article exposes the same Next.js video metadata.
        article_id = self._match_id(url_or_request).removeprefix("a")
        mobile_url = f"https://h5.xinpianchang.com/article/index.html?id={article_id}"
        kwargs["headers"] = {
            **(kwargs.get("headers") or {}),
            "Referer": mobile_url,
            "User-Agent": (
                "Mozilla/5.0 (Linux; Android 15; Pixel 9) AppleWebKit/537.36 "
                "(KHTML, like Gecko) Chrome/153.0.0.0 Mobile Safari/537.36"
            ),
        }
        self.to_screen(f"{video_id}: Desktop page returned {status}; trying the mobile article")
        return super()._download_webpage(mobile_url, video_id, *args, **kwargs)
