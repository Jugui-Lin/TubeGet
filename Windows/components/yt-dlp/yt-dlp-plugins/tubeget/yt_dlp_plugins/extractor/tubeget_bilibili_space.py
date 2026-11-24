from yt_dlp.extractor.bilibili import BilibiliSpaceVideoIE
from yt_dlp.networking.exceptions import HTTPError
from yt_dlp.utils import ExtractorError, parse_duration, traverse_obj


class _TubeGetBilibiliSpaceVideoIE(BilibiliSpaceVideoIE, plugin_name="TubeGet"):
    _SPACE_USER_AGENT = (
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) "
        "AppleWebKit/537.36 (KHTML, like Gecko) Chrome/152.0.0.0 Safari/537.36"
    )

    def _real_extract(self, url):
        creator_id = self._match_id(url)
        public_space = f"https://space.bilibili.com/{creator_id}/video"
        cookies = self._get_cookies(public_space)
        if "buvid3" not in cookies or ("__at_once" not in cookies and "SESSDATA" not in cookies):
            # Open the same public page a browser visits before it requests uploads.
            self._download_webpage(
                public_space, creator_id, note="Opening the public creator space",
                headers={"User-Agent": self._SPACE_USER_AGENT}, fatal=False)
        return super()._real_extract(url)

    def _download_json(self, url_or_request, video_id, *args, **kwargs):
        if isinstance(url_or_request, str) and url_or_request.startswith("https://api.bilibili.com/"):
            kwargs["headers"] = {
                **(kwargs.get("headers") or {}),
                "User-Agent": self._SPACE_USER_AGENT,
                "Accept": "application/json, text/plain, */*",
                "Sec-Fetch-Mode": "cors",
                "Sec-Fetch-Site": "same-site",
                "Sec-Fetch-Dest": "empty",
            }
        is_uploads = url_or_request == "https://api.bilibili.com/x/space/wbi/arc/search"
        try:
            response = super()._download_json(url_or_request, video_id, *args, **kwargs)
        except ExtractorError as error:
            if is_uploads and isinstance(error.cause, HTTPError) and error.cause.status == 412:
                raise self._space_restricted() from error
            raise
        if is_uploads and isinstance(response, dict) and response.get("code") in (-401, -412, -352, -799):
            raise self._space_restricted()
        return response

    @staticmethod
    def _space_restricted():
        return ExtractorError(
            "B 站暂时限制了该空间的投稿列表请求。请稍后重试，"
            "或在「登录选项」读取浏览器 Cookies 或使用应用内登录。", expected=True)

    def _extract_playlist(self, fetch_page, get_metadata, get_entries):
        def labeled_entries(page):
            uploads = {}
            for item in traverse_obj(page, ("list", "vlist", ..., {dict})):
                meta = item.get("meta")
                if isinstance(meta, dict) and meta.get("attribute") == 156:
                    uploads[f'{item.get("mid")}_{meta.get("id")}'] = meta
                elif isinstance(item.get("bvid"), str):
                    uploads[item["bvid"]] = item
            for entry in get_entries(page):
                upload = uploads.get(entry.get("id"))
                if upload:
                    title = upload.get("title") or upload.get("name")
                    if title:
                        entry["title"] = title
                    duration = parse_duration(upload.get("length"))
                    if duration is not None:
                        entry["duration"] = duration
                yield entry

        return super()._extract_playlist(fetch_page, get_metadata, labeled_entries)
