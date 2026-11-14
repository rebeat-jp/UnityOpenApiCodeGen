mergeInto(LibraryManager.library, {
  PvPostReport: function (urlPointer, jsonPointer) {
    var url = UTF8ToString(urlPointer);
    var json = UTF8ToString(jsonPointer);
    fetch(url, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: json })
      .catch(function (error) { console.error('PV_REPORT_POST_FAILED ' + error); });
  }
});
