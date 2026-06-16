#include "chatitemdelegate.h"
#include "config.h"
#include "thememanager.h"
#include <QPainter>
#include <QApplication>
#include <QFontMetrics>
#include <QDebug>
#include <QFile>
#include <QPainterPath>
#include <QCoreApplication>
#include <QSettings>

ChatItemDelegate::ChatItemDelegate(QObject* parent)
    : QStyledItemDelegate(parent)
{
}

void ChatItemDelegate::paint(QPainter* painter, const QStyleOptionViewItem& option,
                             const QModelIndex& index) const
{
    painter->save();

    QStyleOptionViewItem opt = option;
    initStyleOption(&opt, index);

    ThemeManager::Theme currentTheme = ThemeManager::instance().getCurrentTheme();
    bool isDark = (currentTheme == ThemeManager::Dark);

    QColor bgSelected = isDark ? QColor(52, 73, 94) : QColor(228, 242, 255);
    QColor bgHover = isDark ? QColor(60, 60, 60) : QColor(245, 245, 245);
    QColor bgNormal = isDark ? QColor(43, 43, 43) : Qt::white;
    QColor textColor = isDark ? QColor(255, 255, 255) : QColor(0, 0, 0);
    QColor secondaryTextColor = isDark ? QColor(180, 180, 180) : QColor(100, 100, 100);
    QColor separatorColor = isDark ? QColor(60, 60, 60) : QColor(240, 240, 240);
    QColor highlightColor = isDark ? QColor(78, 156, 255) : QColor(0, 120, 215);



    QString title = index.data(ChatListModel::TitleRole).toString();
    QString lastMessage = index.data(ChatListModel::LastMessageRole).toString();
    QString lastTime = index.data(ChatListModel::LastTimeRole).toString();
    QString chatId = index.data(ChatListModel::ChatIdRole).toString();

    if (opt.state & QStyle::State_Selected) {
        painter->fillRect(opt.rect, bgSelected);
        painter->fillRect(opt.rect.left(), opt.rect.top(),
                          4, opt.rect.height(), highlightColor);
    } else if (opt.state & QStyle::State_MouseOver) {
        painter->fillRect(opt.rect, bgHover);
    } else {
        painter->fillRect(opt.rect, bgNormal);
    }

    const int avatarSize = 48;
    const int margin = 8;
    const int spacing = 12;
    const int textRightMargin = 8;

    QRect avatarRect(opt.rect.left() + margin,
                     opt.rect.top() + (opt.rect.height() - avatarSize) / 2,
                     avatarSize, avatarSize);

    painter->setRenderHint(QPainter::Antialiasing);

    QString avatarPath = Config::CHATS_DIR_PATH + "/" + chatId + "/avatar.png";

    if (QFile::exists(avatarPath)) {
        QPixmap avatarPixmap;
        avatarPixmap.load(avatarPath);
        avatarPixmap = avatarPixmap.scaled(avatarSize, avatarSize,
                                           Qt::KeepAspectRatioByExpanding,
                                           Qt::SmoothTransformation);

        QPixmap circularPixmap(avatarSize, avatarSize);
        circularPixmap.fill(Qt::transparent);

        QPainter circlePainter(&circularPixmap);
        circlePainter.setRenderHint(QPainter::Antialiasing);

        QPainterPath clipPath;
        clipPath.addEllipse(0, 0, avatarSize, avatarSize);
        circlePainter.setClipPath(clipPath);

        int x = (avatarSize - avatarPixmap.width()) / 2;
        int y = (avatarSize - avatarPixmap.height()) / 2;
        circlePainter.drawPixmap(x, y, avatarPixmap);

        if (isDark) {
            QPainterPath borderPath;
            borderPath.addEllipse(0, 0, avatarSize, avatarSize);
            circlePainter.setPen(QPen(QColor(80, 80, 80), 1));
            circlePainter.drawPath(borderPath);
        }

        painter->drawPixmap(avatarRect, circularPixmap);
    } else {
        QColor avatarColor = getAvatarColor(title);
        painter->setBrush(avatarColor);
        painter->setPen(Qt::NoPen);
        painter->drawEllipse(avatarRect);

        QString letter = title.isEmpty() ? "?" : title.left(1).toUpper();
        painter->setPen(Qt::white);
        painter->setFont(QFont("Arial", 18, QFont::Bold));
        painter->drawText(avatarRect, Qt::AlignCenter, letter);
    }

    int textAreaLeft = avatarRect.right() + spacing;
    int textAreaWidth = opt.rect.width() - textAreaLeft - textRightMargin;

    QFont titleFont("Arial", 11, QFont::DemiBold);
    QFontMetrics titleMetrics(titleFont);

    QFont timeFont("Arial", 9);
    QFontMetrics timeMetrics(timeFont);

    int timeWidth = timeMetrics.horizontalAdvance(lastTime) + 4;
    if (timeWidth > 60) timeWidth = 60;

    int titleWidth = textAreaWidth - timeWidth - 4;

    QRect titleRect(textAreaLeft,
                    opt.rect.top() + margin + 2,
                    titleWidth,
                    20);

    painter->setPen(textColor);
    painter->setFont(titleFont);

    QString elidedTitle = titleMetrics.elidedText(title, Qt::ElideRight, titleWidth);
    painter->drawText(titleRect, Qt::AlignLeft | Qt::AlignVCenter, elidedTitle);

    if (!lastTime.isEmpty()) {
        QRect timeRect(textAreaLeft + titleWidth + 4,
                       opt.rect.top() + margin + 2,
                       timeWidth,
                       20);

        painter->setPen(secondaryTextColor);
        painter->setFont(timeFont);

        QString elidedTime = timeMetrics.elidedText(lastTime, Qt::ElideRight, timeWidth);
        painter->drawText(timeRect, Qt::AlignRight | Qt::AlignVCenter, elidedTime);
    }

    QRect messageRect(textAreaLeft,
                      titleRect.bottom() + 4,
                      textAreaWidth,
                      opt.rect.height() - titleRect.height() - margin * 2 - 4);

    if (!lastMessage.isEmpty()) {
        painter->setPen(secondaryTextColor);
        painter->setFont(QFont("Arial", 10));

        QFontMetrics messageMetrics(painter->font());

        QString displayMessage = lastMessage;
        displayMessage = displayMessage.replace('\n', ' ').simplified();

        if (displayMessage.length() > 50) {
            displayMessage = displayMessage.left(47) + "...";
        }

        QString elidedMessage = messageMetrics.elidedText(displayMessage, Qt::ElideRight,
                                                          messageRect.width());
        painter->drawText(messageRect, Qt::AlignLeft | Qt::AlignTop, elidedMessage);
    }

    painter->setPen(QPen(separatorColor, 1));
    painter->drawLine(opt.rect.left() + margin, opt.rect.bottom(),
                      opt.rect.right() - margin, opt.rect.bottom());

    painter->restore();
}

QSize ChatItemDelegate::sizeHint(const QStyleOptionViewItem& option,
                                 const QModelIndex& index) const
{
    Q_UNUSED(option);
    Q_UNUSED(index);
    return QSize(200, 72);
}

QColor ChatItemDelegate::getAvatarColor(const QString& text) const
{
    uint hash = qHash(text);

    QColor colors[] = {
        QColor(66, 133, 244),   
        QColor(219, 68, 55),    
        QColor(244, 160, 0),    
        QColor(15, 157, 88),    
        QColor(171, 71, 188),   
        QColor(0, 172, 193),    
        QColor(255, 112, 67),   
        QColor(121, 85, 72)     
    };

    return colors[hash % 8];
}