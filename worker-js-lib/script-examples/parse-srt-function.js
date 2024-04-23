function parseSRT(srtContent) {
  const subtitles = [];
  const lines = srtContent.trim().split("\n");

  for (let i = 0; i < lines.length; i++) {
    const line = lines[i].trim();

    // Check if the line is a subtitle number
    if (/^\d+$/.test(line)) {
      const entry = { text: "", startTime: null, endTime: null };

      // Extract and parse the timecodes
      const timecodeLine = lines[i + 1].trim().split(" --> ");
      entry.startTime = parseSRTTimecode(timecodeLine[0]);
      entry.endTime = parseSRTTimecode(timecodeLine[1]);

      // Extract and concatenate the subtitle text
      let m = i;
      for (let j = i + 2; j < lines.length; j++) {
        if (lines[j].trim() === "") {
          // Empty line marks the end of the subtitle text
          break;
        }
        entry.text += lines[j].trim() + " ";
        m = j;
      }

      // Add the subtitle entry to the list
      subtitles.push(entry);

      // Move the iterator to the next subtitle entry
      i = m;
    }
  }

  return subtitles;
}

// Function to parse an SRT timecode (HH:MM:SS,MMM format)
function parseSRTTimecode(timecode) {
  const parts = timecode.split(/[,:]/).map(parseFloat);
  if (parts.length === 4) {
    const [hours, minutes, seconds, milliseconds] = parts;
    return hours * 3600 + minutes * 60 + seconds + milliseconds / 1000.0;
  }
  return null;
}
