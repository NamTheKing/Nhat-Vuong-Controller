using System.Text;
using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using NhatVuong.Adapters.Rest;
using NhatVuong.Application.Commands;
using NhatVuong.Application.Scheduling;
using NhatVuong.Application.Tests.Support;
using NhatVuong.Application.Timetables;
using NhatVuong.Domain;

namespace NhatVuong.Application.Tests;

/// <summary>US-20 / US-02: timetable import, per-row errors, atomic replacement and rights derived from it.</summary>
public class TimetableImportTests
{
    [Fact(DisplayName = "US-20-1: a valid file imports every row, reports the count, and grants the lecturer the room")]
    public async Task ValidImport()
    {
        await using var h = new TestHarness();
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        var room = await h.AddRoomAsync("A101");
        var device = await h.AddDeviceAsync(room, "HW-1");

        var result = await h.Service<TimetableImportService>().ImportAsync(
        [
            new TimetableRow(2, "C1", "a101", "GV@u.edu.vn", "2026-10-05 07:30", "2026-10-05 09:30"),
            new TimetableRow(3, "C2", "A101", "gv@u.edu.vn", "05/10/2026 13:00", "05/10/2026 15:00"),
        ]);

        Assert.True(result.Success);
        Assert.Equal(2, result.ImportedCount);
        Assert.Equal(2, await h.WithDbAsync(db => db.AccessGrants.CountAsync()));
        var first = await h.WithDbAsync(db => db.TimetableEntries.SingleAsync(e => e.ExternalId == "C1"));
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 0, 30, 0, TimeSpan.Zero), first.StartsAt);

        // Rights now flow from the import (US-02-1): 08:00 is inside the 07:30-09:30 class.
        var outcome = await h.Service<CommandService>().ExecuteAsync(
            new CommandRequest(TestHarness.ActorOf(lecturer), device.Id, CommandAction.PowerOn, null));
        Assert.Equal(CommandResult.Succeeded, outcome.Result);
    }

    [Fact(DisplayName = "US-20-2: invalid rows are listed by row number and nothing is written")]
    public async Task InvalidRowsWriteNothing()
    {
        await using var h = new TestHarness();
        await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        await h.AddUserAsync(UserRole.ClassMonitor, "lt@u.edu.vn");
        await h.AddRoomAsync("A101");

        var result = await h.Service<TimetableImportService>().ImportAsync(
        [
            new TimetableRow(2, "C1", "A101", "gv@u.edu.vn", "2026-10-05 07:30", "2026-10-05 09:30"),
            new TimetableRow(3, "C2", "Z999", "gv@u.edu.vn", "2026-10-05 10:00", "2026-10-05 11:00"),
            new TimetableRow(4, "C3", "A101", "nobody@u.edu.vn", "2026-10-05 10:00", "2026-10-05 11:00"),
            new TimetableRow(5, "C4", "A101", "lt@u.edu.vn", "2026-10-05 12:00", "2026-10-05 13:00"),
            new TimetableRow(6, "C5", "A101", "gv@u.edu.vn", "tomorrow", "2026-10-05 13:00"),
            new TimetableRow(7, "C6", "A101", "gv@u.edu.vn", "2026-10-05 15:00", "2026-10-05 14:00"),
            new TimetableRow(8, "C1", "A101", "gv@u.edu.vn", "2026-10-06 07:30", "2026-10-06 09:30"),
            new TimetableRow(9, "C8", "A101", "gv@u.edu.vn", "2026-10-05 09:00", "2026-10-05 10:00"),
            new TimetableRow(10, "", "A101", "gv@u.edu.vn", "2026-10-07 09:00", "2026-10-07 10:00"),
        ]);

        Assert.False(result.Success);
        Assert.Equal(0, result.ImportedCount);
        var byRow = result.Errors.ToLookup(e => e.Row, e => e.Code);
        Assert.Contains("RoomNotFound", byRow[3]);
        Assert.Contains("LecturerNotFound", byRow[4]);
        Assert.Contains("NotALecturer", byRow[5]);
        Assert.Contains("InvalidDateTime", byRow[6]);
        Assert.Contains("EndBeforeStart", byRow[7]);
        Assert.Contains("DuplicateTimetableId", byRow[8]);
        Assert.Contains("RoomDoubleBooked", byRow[9]);
        Assert.Contains("MissingField", byRow[10]);
        Assert.Empty(byRow[2]);
        Assert.Equal(0, await h.WithDbAsync(db => db.TimetableEntries.CountAsync()));
        Assert.Equal(0, await h.WithDbAsync(db => db.AccessGrants.CountAsync()));
    }

    [Fact(DisplayName = "US-20: re-import atomically replaces the covered days, revokes old grants and cancels their pre-cools")]
    public async Task ReimportReplaces()
    {
        await using var h = new TestHarness();
        h.SetLocalTime(6, 0);
        var lecturer = await h.AddUserAsync(UserRole.Lecturer, "gv@u.edu.vn");
        await h.AddRoomAsync("A101");
        var import = h.Service<TimetableImportService>();
        await import.ImportAsync([new TimetableRow(2, "OLD", "A101", "gv@u.edu.vn", "2026-10-05 08:00", "2026-10-05 10:00")]);
        var old = await h.WithDbAsync(db => db.TimetableEntries.SingleAsync());
        var preCool = await h.Service<SchedulingService>().CreatePreCoolAsync(TestHarness.ActorOf(lecturer), old.Id, 10);

        var result = await h.Service<TimetableImportService>().ImportAsync(
            [new TimetableRow(2, "NEW", "A101", "gv@u.edu.vn", "2026-10-05 13:00", "2026-10-05 15:00")]);

        Assert.Equal(1, result.ReplacedCount);
        Assert.Equal("NEW", (await h.WithDbAsync(db => db.TimetableEntries.SingleAsync())).ExternalId);
        var grants = await h.WithDbAsync(db => db.AccessGrants.ToListAsync());
        Assert.Contains(grants, g => g.RevokedReason == "TimetableReplaced");
        Assert.Contains(grants, g => g.RevokedAt is null);
        Assert.Equal(PreCoolStatus.Cancelled, (await h.WithDbAsync(db => db.PreCoolSchedules.SingleAsync(p => p.Id == preCool.Id))).Status);
    }

    [Fact(DisplayName = "FR-F2: CSV with BOM, quotes and blank lines is read with file row numbers")]
    public async Task CsvReader()
    {
        var csv = "﻿timetable_id,room_code,lecturer_email,starts_at,ends_at\r\n"
                  + "C1,A101,gv@u.edu.vn,2026-10-05 07:30,2026-10-05 09:30\r\n"
                  + "\r\n"
                  + "\"C,2\",A101,\"gv@u.edu.vn\",2026-10-05 10:00,2026-10-05 11:00\r\n";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));

        var rows = await TimetableFileReader.ReadAsync(stream, "tkb.csv", CancellationToken.None);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows[0].RowNumber);
        Assert.Equal(4, rows[1].RowNumber);
        Assert.Equal("C,2", rows[1].TimetableId);
    }

    [Fact(DisplayName = "FR-F2: Excel files are read, including native date cells")]
    public async Task ExcelReader()
    {
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("TKB");
        string[] header = ["timetable_id", "room_code", "lecturer_email", "starts_at", "ends_at"];
        for (var i = 0; i < header.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = header[i];
        }

        sheet.Cell(2, 1).Value = "C1";
        sheet.Cell(2, 2).Value = "A101";
        sheet.Cell(2, 3).Value = "gv@u.edu.vn";
        sheet.Cell(2, 4).Value = new DateTime(2026, 10, 5, 7, 30, 0);
        sheet.Cell(2, 5).Value = "2026-10-05 09:30";
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;

        var rows = await TimetableFileReader.ReadAsync(stream, "tkb.xlsx", CancellationToken.None);

        var row = Assert.Single(rows);
        Assert.Equal("2026-10-05 07:30:00", row.StartsAt);
    }

    [Fact(DisplayName = "FR-F3: a file missing a required column is rejected before import")]
    public async Task MissingColumn()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("timetable_id,room_code\nC1,A101\n"));
        var error = await Assert.ThrowsAsync<ValidationException>(() => TimetableFileReader.ReadAsync(stream, "x.csv", CancellationToken.None));
        Assert.Equal("MissingColumns", error.Code);
        await Assert.ThrowsAsync<ValidationException>(() => TimetableFileReader.ReadAsync(new MemoryStream([1]), "x.pdf", CancellationToken.None));
    }
}
