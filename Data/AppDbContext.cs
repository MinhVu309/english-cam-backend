using EnglishCam.Api.Models;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace EnglishCam.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<AppUser>(options)
{
    public DbSet<OtpCode> OtpCodes => Set<OtpCode>();
    public DbSet<RevokedToken> RevokedTokens => Set<RevokedToken>();
    public DbSet<ChildProfile> Children => Set<ChildProfile>();
    public DbSet<ChildSession> ChildSessions => Set<ChildSession>();
    public DbSet<ChildLinkCode> ChildLinkCodes => Set<ChildLinkCode>();
    public DbSet<VocabCategory> VocabCategories => Set<VocabCategory>();
    public DbSet<VocabWord> VocabWords => Set<VocabWord>();
    public DbSet<MediaFile> MediaFiles => Set<MediaFile>();
    public DbSet<VocabRelease> VocabReleases => Set<VocabRelease>();
    public DbSet<Badge> Badges => Set<Badge>();
    public DbSet<ChildBadge> ChildBadges => Set<ChildBadge>();
    public DbSet<ChildStats> ChildStats => Set<ChildStats>();
    public DbSet<ChildWordProgress> WordProgress => Set<ChildWordProgress>();
    public DbSet<GameSession> GameSessions => Set<GameSession>();
    public DbSet<GameAnswer> GameAnswers => Set<GameAnswer>();
    public DbSet<DailyActivity> DailyActivities => Set<DailyActivity>();
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();
    public DbSet<RecognitionFeedback> RecognitionFeedback => Set<RecognitionFeedback>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);
        b.Entity<OtpCode>().HasIndex(o => o.UserId);
        b.Entity<OtpCode>().HasOne<AppUser>().WithMany().HasForeignKey(o => o.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RecognitionFeedback>().HasOne<AppUser>().WithMany().HasForeignKey(f => f.UserId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<RecognitionFeedback>().HasIndex(f => f.UserId);
        b.Entity<RecognitionFeedback>().HasIndex(f => f.CreatedAt);
        b.Entity<RecognitionFeedback>().Property(f => f.PredictedLabel).HasMaxLength(40);
        b.Entity<RecognitionFeedback>().Property(f => f.CorrectWordId).HasMaxLength(40);
        b.Entity<RecognitionFeedback>().Property(f => f.CorrectText).HasMaxLength(100);
        b.Entity<RecognitionFeedback>().Property(f => f.Source).HasMaxLength(10);
        b.Entity<RecognitionFeedback>().Property(f => f.ModelSource).HasMaxLength(20);
        b.Entity<RevokedToken>().HasKey(r => r.Jti);
        b.Entity<RevokedToken>().Property(r => r.Jti).HasMaxLength(64);

        // Xoá phụ huynh thì xoá hồ sơ con (cascade). UserId của bé dùng NoAction để tránh nhiều đường cascade trong SQL Server;
        // ChildService xoá AppUser của bé bằng tay sau khi xoá hồ sơ.
        b.Entity<ChildProfile>().HasOne(c => c.Parent).WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChildProfile>().HasOne(c => c.User).WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.NoAction);
        b.Entity<ChildProfile>().HasIndex(c => c.UserId).IsUnique();
        b.Entity<ChildProfile>().HasIndex(c => c.ParentId);
        b.Entity<ChildProfile>().Property(c => c.Nickname).HasMaxLength(30);
        b.Entity<ChildProfile>().Property(c => c.AvatarKey).HasMaxLength(30);
        b.Entity<ChildSession>().HasOne(s => s.Child).WithMany(c => c.Sessions).HasForeignKey(s => s.ChildId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChildSession>().HasIndex(s => new { s.ChildId, s.DeviceId });
        b.Entity<ChildSession>().Property(s => s.DeviceId).HasMaxLength(200);
        b.Entity<ChildSession>().Property(s => s.DeviceName).HasMaxLength(100);
        b.Entity<ChildLinkCode>().HasOne(l => l.Child).WithMany().HasForeignKey(l => l.ChildId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChildLinkCode>().HasIndex(l => l.CodeHash).IsUnique();
        b.Entity<ChildLinkCode>().Property(l => l.CodeHash).HasMaxLength(64);

        b.Entity<VocabCategory>().Property(c => c.Id).HasMaxLength(40);
        b.Entity<VocabCategory>().Property(c => c.Vi).HasMaxLength(100);
        b.Entity<VocabCategory>().Property(c => c.En).HasMaxLength(100);
        b.Entity<VocabCategory>().Property(c => c.Emoji).HasMaxLength(20);
        b.Entity<VocabCategory>().Property(c => c.Color).HasMaxLength(7);

        b.Entity<VocabWord>().Property(w => w.Id).HasMaxLength(40);
        b.Entity<VocabWord>().Property(w => w.CategoryId).HasMaxLength(40);
        b.Entity<VocabWord>().Property(w => w.Label).HasMaxLength(40);
        b.Entity<VocabWord>().Property(w => w.En).HasMaxLength(100);
        b.Entity<VocabWord>().Property(w => w.Vi).HasMaxLength(100);
        b.Entity<VocabWord>().Property(w => w.Phonetic).HasMaxLength(100);
        b.Entity<VocabWord>().Property(w => w.ExampleEn).HasMaxLength(300);
        b.Entity<VocabWord>().Property(w => w.ExampleVi).HasMaxLength(300);
        b.Entity<VocabWord>().Property(w => w.Audio).HasMaxLength(200);
        b.Entity<VocabWord>().Property(w => w.ExampleAudio).HasMaxLength(200);
        b.Entity<VocabWord>().Property(w => w.Image).HasMaxLength(200);
        b.Entity<VocabWord>().Property(w => w.Emoji).HasMaxLength(20);
        b.Entity<VocabWord>().HasOne(w => w.Category).WithMany().HasForeignKey(w => w.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<VocabWord>().HasIndex(w => w.CategoryId);
        // Mỗi nhãn của model chỉ gắn với một từ còn dùng (từ đã xoá mềm không tính).
        b.Entity<VocabWord>().HasIndex(w => w.Label).IsUnique().HasFilter("[Label] IS NOT NULL AND [IsDeleted] = 0");

        b.Entity<MediaFile>().HasKey(m => m.Path);
        b.Entity<MediaFile>().Property(m => m.Path).HasMaxLength(200);
        b.Entity<MediaFile>().Property(m => m.Sha256).HasMaxLength(64);
        b.Entity<MediaFile>().Property(m => m.ContentType).HasMaxLength(50);

        b.Entity<VocabRelease>().HasKey(r => r.Version);
        b.Entity<VocabRelease>().Property(r => r.Version).ValueGeneratedNever();
        b.Entity<VocabRelease>().Property(r => r.Notes).HasMaxLength(500);

        // Tiến độ của bé: xoá hồ sơ bé thì xoá hết (cascade). Từ vựng không bao giờ xoá cứng nên FK tới từ dùng Restrict.
        b.Entity<Badge>().Property(x => x.Id).HasMaxLength(40);
        b.Entity<Badge>().Property(x => x.Name).HasMaxLength(60);
        b.Entity<Badge>().Property(x => x.Description).HasMaxLength(200);
        b.Entity<Badge>().Property(x => x.Emoji).HasMaxLength(20);
        b.Entity<Badge>().Property(x => x.Metric).HasMaxLength(20);

        b.Entity<ChildBadge>().HasKey(x => new { x.ChildId, x.BadgeId });
        b.Entity<ChildBadge>().Property(x => x.BadgeId).HasMaxLength(40);
        b.Entity<ChildBadge>().HasOne<ChildProfile>().WithMany().HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChildBadge>().HasOne<Badge>().WithMany().HasForeignKey(x => x.BadgeId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ChildStats>().HasKey(x => x.ChildId);
        b.Entity<ChildStats>().HasOne<ChildProfile>().WithOne().HasForeignKey<ChildStats>(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ChildWordProgress>().HasKey(x => new { x.ChildId, x.WordId });
        b.Entity<ChildWordProgress>().Property(x => x.WordId).HasMaxLength(40);
        b.Entity<ChildWordProgress>().HasOne<ChildProfile>().WithMany().HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ChildWordProgress>().HasOne<VocabWord>().WithMany().HasForeignKey(x => x.WordId).OnDelete(DeleteBehavior.Restrict);

        b.Entity<GameSession>().HasKey(x => x.Id);
        b.Entity<GameSession>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<GameSession>().HasOne<ChildProfile>().WithMany().HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<GameSession>().HasIndex(x => new { x.ChildId, x.StartedAt });
        b.Entity<GameAnswer>().Property(x => x.WordId).HasMaxLength(40);
        b.Entity<GameAnswer>().HasOne<GameSession>().WithMany(x => x.Answers).HasForeignKey(x => x.SessionId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<DailyActivity>().HasKey(x => new { x.ChildId, x.Date });
        b.Entity<DailyActivity>().HasOne<ChildProfile>().WithMany().HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);

        b.Entity<ProcessedEvent>().HasKey(x => x.Id);
        b.Entity<ProcessedEvent>().Property(x => x.Id).ValueGeneratedNever();
        b.Entity<ProcessedEvent>().HasOne<ChildProfile>().WithMany().HasForeignKey(x => x.ChildId).OnDelete(DeleteBehavior.Cascade);
        b.Entity<ProcessedEvent>().HasIndex(x => x.ChildId);
    }
}
