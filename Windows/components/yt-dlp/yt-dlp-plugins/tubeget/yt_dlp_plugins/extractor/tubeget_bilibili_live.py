from yt_dlp.extractor.bilibili import BiliLiveIE


class _TubeGetBiliLiveIE(BiliLiveIE, plugin_name='TubeGet'):
    def _parse_formats(self, qn, fmt):
        for stream in super()._parse_formats(qn, fmt):
            if stream.get('ext') == 'fmp4':
                # fMP4 describes the HLS segments. The output container is MP4.
                stream.update(ext='mp4', protocol='m3u8')
            yield stream
