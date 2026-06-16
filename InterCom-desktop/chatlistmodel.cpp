#include "chatlistmodel.h"
#include <QMimeData>
#include <QByteArray>
#include <QDataStream>

ChatListModel::ChatListModel(QObject* parent)
    : QAbstractListModel(parent)
{
}

int ChatListModel::rowCount(const QModelIndex& parent) const
{
    Q_UNUSED(parent);
    return m_chats.size();
}

QVariant ChatListModel::data(const QModelIndex& index, int role) const
{
    if (!index.isValid() || index.row() >= m_chats.size())
        return {};

    const ChatItem& item = m_chats[index.row()];

    if (role == Qt::DisplayRole)
        return item.title;

    if (role == ChatIdRole)
        return item.chatId;
    if (role == TitleRole)
        return item.title;
    if (role == LastMessageRole)
        return item.lastMessage;
    if (role == LastTimeRole) {
        if (item.lastTime.isValid()) {
            QDate today = QDate::currentDate();
            QDate messageDate = item.lastTime.date();

            if (messageDate == today) {
                return item.lastTime.toString("HH:mm");
            } else if (messageDate == today.addDays(-1)) {
                return "вчера";
            } else if (messageDate.year() == today.year()) {
                return item.lastTime.toString("dd.MM");
            } else {
                return item.lastTime.toString("dd.MM.yy");
            }
        }
        return "";
    }

    return {};
}

void ChatListModel::setChats(const QVector<ChatItem>& chats)
{
    beginResetModel();
    m_chats = chats;
    endResetModel();
}

QHash<int, QByteArray> ChatListModel::roleNames() const
{
    return {
        {ChatIdRole, "chatId"},
        {TitleRole, "title"},
        {LastMessageRole, "lastMessage"},
        {LastTimeRole, "lastTime"}
    };
}

Qt::ItemFlags ChatListModel::flags(const QModelIndex &index) const
{
    Qt::ItemFlags defaultFlags = QAbstractListModel::flags(index);

    if (index.isValid()) {
        return Qt::ItemIsDragEnabled | Qt::ItemIsDropEnabled | defaultFlags;
    } else {
        return Qt::ItemIsDropEnabled | defaultFlags;
    }
}

Qt::DropActions ChatListModel::supportedDropActions() const
{
    return Qt::MoveAction; 
}

bool ChatListModel::moveRows(const QModelIndex &sourceParent, int sourceRow, int count,
                             const QModelIndex &destinationParent, int destinationRow)
{
    if (count != 1 || sourceRow < 0 || sourceRow >= m_chats.size() ||
        destinationRow < 0 || destinationRow > m_chats.size() ||
        sourceParent != destinationParent) {
        return false;
    }

    if (sourceRow == destinationRow) {
        return false;
    }

    if (destinationRow >= sourceRow && destinationRow <= sourceRow + count) {
        return false;
    }

    beginMoveRows(sourceParent, sourceRow, sourceRow, destinationParent, destinationRow);

    ChatItem item = m_chats.at(sourceRow);

    m_chats.removeAt(sourceRow);

    int insertPos = destinationRow;
    if (destinationRow > sourceRow) {
        insertPos = destinationRow - 1;
    }

    m_chats.insert(insertPos, item);

    endMoveRows();

    QModelIndex parentIndex = QModelIndex();
    emit dataChanged(index(0, 0, parentIndex), index(rowCount(parentIndex) - 1, 0, parentIndex));
    emit chatOrderChanged();

    return true;
}


void ChatListModel::setChatsOrdered(const QVector<ChatItem>& chats)
{
    beginResetModel();
    m_chats = chats;
    endResetModel();
}

bool ChatListModel::dropMimeData(const QMimeData *data, Qt::DropAction action,int row, int column, const QModelIndex &parent)
{
    Q_UNUSED(column);

    if (action == Qt::IgnoreAction) {
        return true;
    }

    if (action == Qt::MoveAction) {
        return QAbstractItemModel::dropMimeData(data, action, row, 0, parent);
    }

    return false;
}
